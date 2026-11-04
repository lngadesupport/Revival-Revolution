using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public partial class OnlineMatchWindow : Window
{
    private readonly RankedApiClient _api = new();
    private readonly RankedCredentialStore _credentials = new();
    private CancellationTokenSource? _polling;
    private string? _token;
    private string? _playerId;
    private bool _queued;
    private string _queueMode = "casual";
    private bool _roomWaiting;
    private string _ratingText = "—";
    private string _rankedDetails = "";

    public OnlineMatchWindow() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var saved = RankedApiClient.LoadEndpoint();
        if (!string.IsNullOrWhiteSpace(saved)) ApiUrlBox.Text = saved;
        if (!string.IsNullOrWhiteSpace(ApiUrlBox.Text)) await RestoreSessionAsync();
    }

    private async Task RestoreSessionAsync()
    {
        try
        {
            var endpoint = RankedApiClient.ValidateEndpoint(ApiUrlBox.Text);
            _token = _credentials.Read(endpoint);
            if (string.IsNullOrWhiteSpace(_token)) return;
            _api.Configure(endpoint, _token);
            ApplyProfile(await _api.GetAsync("/v1/me"));
            await RefreshRankedSummaryAsync();
            StatusText.Text = "Conta autenticada. Escolha uma modalidade de matchmaking.";
        }
        catch (Exception ex)
        {
            _token = null;
            ProfileText.Text = "Não foi possível restaurar a sessão.";
            StatusText.Text = ex.Message;
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            var endpoint = RankedApiClient.ValidateEndpoint(ApiUrlBox.Text);
            if (string.IsNullOrWhiteSpace(NicknameBox.Text))
                throw new InvalidOperationException("Informe um nickname para criar ou identificar a conta.");
            RankedApiClient.SaveEndpoint(endpoint);
            _token = _credentials.Read(endpoint);
            _api.Configure(endpoint, _token);
            if (string.IsNullOrWhiteSpace(_token))
            {
                var created = await _api.PostAsync("/v1/register", new JsonObject
                {
                    ["display_name"] = NicknameBox.Text.Trim()
                }, authenticated: false);
                _token = created["token"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("O servidor não devolveu o token da conta.");
                _playerId = created["player_id"]?.GetValue<string>();
                _credentials.Write(endpoint, _token);
                _api.Configure(endpoint, _token);
                ApplyProfile(created);
                await RefreshRankedSummaryAsync();
                StatusText.Text = "Conta criada e credencial protegida no Gerenciador de Credenciais do Windows.";
            }
            else
            {
                ApplyProfile(await _api.GetAsync("/v1/me"));
                await RefreshRankedSummaryAsync();
                StatusText.Text = "Sessão restaurada com segurança.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao conectar: " + ex.Message;
            ProfileText.Text = "Conta não conectada.";
            SetModeButtons(false);
        }
        finally { ConnectButton.IsEnabled = true; }
    }

    private void ApplyProfile(JsonNode? profile)
    {
        if (profile is null) return;
        _playerId = profile["player_id"]?.GetValue<string>() ?? _playerId;
        var name = profile["display_name"]?.GetValue<string>() ?? NicknameBox.Text;
        _ratingText = profile["rating"]?.ToString() ?? "—";
        NicknameBox.Text = name;
        RenderProfile();
        SetModeButtons(!string.IsNullOrWhiteSpace(_token));
    }

    private void RenderProfile()
    {
        var name = NicknameBox.Text;
        ProfileText.Text = $"{name}  •  Rating {_ratingText}{_rankedDetails}\nID: {_playerId ?? "—"}";
    }

    private async Task RefreshRankedSummaryAsync()
    {
        try
        {
            var summary = await _api.GetAsync("/v1/ranked/me");
            _ratingText = summary["rating"]?.ToString() ?? _ratingText;
            var division = summary["division"]?.GetValue<string>() ?? "Sem divisão";
            var wins = summary["wins"]?.ToString() ?? "0";
            var losses = summary["losses"]?.ToString() ?? "0";
            var draws = summary["draws"]?.ToString() ?? "0";
            _rankedDetails = $"\n{division}  •  V {wins} / D {losses} / E {draws}";
            RenderProfile();
        }
        catch (Exception)
        {
            // Keep account login usable against older API deployments.
            _rankedDetails = "\nDados Ranked indisponíveis";
            RenderProfile();
        }
    }

    private void SetModeButtons(bool enabled)
    {
        SearchButton.IsEnabled = enabled && !_queued && !_roomWaiting;
        RankedSearchButton.IsEnabled = enabled && !_queued && !_roomWaiting;
        CreateRoomButton.IsEnabled = enabled && !_queued && !_roomWaiting;
        JoinRoomButton.IsEnabled = enabled && !_queued && !_roomWaiting;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token) || _roomWaiting) return;
        _queueMode = (sender as FrameworkElement)?.Tag?.ToString() == "ranked" ? "ranked" : "casual";
        SearchButton.IsEnabled = false;
        RankedSearchButton.IsEnabled = false;
        CreateRoomButton.IsEnabled = false;
        JoinRoomButton.IsEnabled = false;
        try
        {
            var queueEndpoint = _queueMode == "ranked" ? "/v1/ranked/queue/join" : "/v1/queue/join";
            var response = await _api.PostAsync(queueEndpoint, new JsonObject { ["region"] = "sa-east" });
            if (response["match"] is JsonObject immediate) { ShowMatch(immediate); return; }
            _queued = true;
            if (_queueMode == "ranked")
            {
                RankedQueueStateText.Text = "PROCURANDO ADVERSÁRIO";
                RankedQueueDetailText.Text = "Fila ranqueada ativa. O servidor está buscando um adversário com rating compatível.";
                RankedCancelButton.IsEnabled = true;
            }
            else
            {
                QueueStateText.Text = "PROCURANDO ADVERSÁRIO";
                QueueDetailText.Text = "Fila não ranqueada ativa. Encontre outro jogador sem alterar seu rating.";
                CancelButton.IsEnabled = true;
            }
            _polling = new CancellationTokenSource();
            _ = PollQueueAsync(_polling.Token);
        }
        catch (Exception ex)
        {
            SetModeButtons(!string.IsNullOrWhiteSpace(_token));
            StatusText.Text = "Não foi possível entrar na fila: " + ex.Message;
        }
    }

    private async Task PollQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _queued)
            {
                var queuePrefix = _queueMode == "ranked" ? "/v1/ranked" : "/v1";
                var matchResult = await _api.GetAsync($"{queuePrefix}/match", cancellationToken);
                if (matchResult["match"] is JsonObject match)
                {
                    await Dispatcher.InvokeAsync(() => ShowMatch(match));
                    return;
                }
                var queue = await _api.GetAsync($"{queuePrefix}/queue", cancellationToken);
                await Dispatcher.InvokeAsync(() =>
                {
                    var position = queue["position"]?.ToString() ?? "—";
                    var detail = queue["queued"]?.GetValue<bool>() == true
                        ? $"Fila ativa • posição {position} • aguardando adversário."
                        : "O servidor não informa uma posição ativa na fila.";
                    if (_queueMode == "ranked") RankedQueueDetailText.Text = detail;
                    else QueueDetailText.Text = detail;
                });
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _queued = false;
                StatusText.Text = "A consulta da fila falhou: " + ex.Message;
                CancelButton.IsEnabled = false;
                RankedCancelButton.IsEnabled = false;
                SetModeButtons(!string.IsNullOrWhiteSpace(_token));
            });
        }
    }

    private async void CreateRoom_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token) || _queued || _roomWaiting) return;
        CreateRoomButton.IsEnabled = false;
        JoinRoomButton.IsEnabled = false;
        try
        {
            var result = await _api.PostAsync("/v1/rooms/create", new JsonObject { ["region"] = "sa-east" });
            var room = result["room"] as JsonObject
                ?? throw new InvalidOperationException("O servidor não devolveu os dados da sala.");
            var code = room["room_code"]?.GetValue<string>()
                ?? throw new InvalidOperationException("O servidor não devolveu o código da sala.");
            RoomCodeDisplayBox.Text = code;
            RoomStateText.Text = "Sala aberta. Compartilhe este código com seu adversário. Ela expira em 15 minutos.";
            _roomWaiting = true;
            CancelRoomButton.IsEnabled = true;
            StatusText.Text = "Sala privada criada. Aguardando o outro jogador inserir o código.";
            _polling = new CancellationTokenSource();
            _ = PollRoomAsync(_polling.Token);
        }
        catch (Exception ex)
        {
            SetModeButtons(!string.IsNullOrWhiteSpace(_token));
            StatusText.Text = "Não foi possível criar a sala: " + ex.Message;
        }
    }

    private async Task PollRoomAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _roomWaiting)
            {
                var result = await _api.GetAsync("/v1/rooms/current", cancellationToken);
                if (result["match"] is JsonObject match)
                {
                    await Dispatcher.InvokeAsync(() => ShowMatch(match));
                    return;
                }
                if (result["room"] is not JsonObject room ||
                    room["status"]?.GetValue<string>() != "waiting")
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _roomWaiting = false;
                        CancelRoomButton.IsEnabled = false;
                        RoomStateText.Text = "A sala expirou ou foi encerrada.";
                        SetModeButtons(!string.IsNullOrWhiteSpace(_token));
                    });
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _roomWaiting = false;
                CancelRoomButton.IsEnabled = false;
                RoomStateText.Text = "Não foi possível consultar a sala.";
                StatusText.Text = "Falha ao consultar a sala: " + ex.Message;
                SetModeButtons(!string.IsNullOrWhiteSpace(_token));
            });
        }
    }

    private async void JoinRoom_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token) || _queued || _roomWaiting) return;
        var code = RoomCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            StatusText.Text = "Digite o código de 6 caracteres recebido do anfitrião.";
            return;
        }
        JoinRoomButton.IsEnabled = false;
        try
        {
            var result = await _api.PostAsync("/v1/rooms/join", new JsonObject { ["code"] = code });
            var match = result["match"] as JsonObject
                ?? throw new InvalidOperationException("A sala não devolveu a partida.");
            ShowMatch(match);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível entrar na sala: " + ex.Message;
            JoinRoomButton.IsEnabled = !string.IsNullOrWhiteSpace(_token);
        }
    }

    private void ShowMatch(JsonObject match)
    {
        _queued = false;
        _roomWaiting = false;
        _polling?.Cancel();
        CancelButton.IsEnabled = false;
        RankedCancelButton.IsEnabled = false;
        CancelRoomButton.IsEnabled = false;
        SetModeButtons(false);
        if (_queueMode == "ranked") RankedQueueStateText.Text = "PARTIDA RANQUEADA ENCONTRADA";
        else QueueStateText.Text = "PARTIDA CASUAL ENCONTRADA";
        RoomStateText.Text = "Partida privada atribuída.";
        var opponent = match["opponent"]?["display_name"]?.GetValue<string>() ?? "Adversário";
        var rating = match["opponent"]?["rating"]?.ToString() ?? "—";
        var matchId = match["match_id"]?.GetValue<string>() ?? "—";
        var slot = match["player_slot"]?.ToString() ?? "—";
        MatchTitleText.Text = opponent;
        MatchDetailText.Text = $"Adversário: {opponent}\nRating: {rating}\nSua posição: Player {(slot == "0" ? "1" : slot == "1" ? "2" : slot)}\nPartida: {matchId}";
        if (_queueMode == "ranked") RankedQueueDetailText.Text = "O servidor atribuiu seu adversário para uma partida ranqueada.";
        else QueueDetailText.Text = "O servidor atribuiu seu adversário para uma partida casual.";
        StatusText.Text = "Matchmaking concluído. A sessão P2P ainda não foi iniciada: falta emitir e validar o ticket de conexão.";
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _polling?.Cancel();
        try
        {
            var matchPath = _queueMode == "ranked" ? "/v1/ranked/match" : "/v1/match";
            var current = await _api.GetAsync(matchPath);
            if (current["match"] is JsonObject assignedMatch)
            {
                ShowMatch(assignedMatch);
                return;
            }
            await _api.PostAsync("/v1/queue/leave", new JsonObject());
            _queued = false;
            if (_queueMode == "ranked")
            {
                RankedQueueStateText.Text = "BUSCA RANQUEADA CANCELADA";
                RankedQueueDetailText.Text = "Você saiu da fila ranqueada.";
            }
            else
            {
                QueueStateText.Text = "BUSCA CANCELADA";
                QueueDetailText.Text = "Você saiu da fila casual.";
            }
            StatusText.Text = "Busca cancelada.";
        }
        catch (Exception ex) { StatusText.Text = "Falha ao cancelar a busca: " + ex.Message; }
        finally
        {
            CancelButton.IsEnabled = false;
            RankedCancelButton.IsEnabled = false;
            SetModeButtons(!string.IsNullOrWhiteSpace(_token));
        }
    }

    private async void CancelRoom_Click(object sender, RoutedEventArgs e)
    {
        _polling?.Cancel();
        try
        {
            var result = await _api.PostAsync("/v1/rooms/cancel", new JsonObject());
            _roomWaiting = false;
            RoomCodeDisplayBox.Text = "------";
            RoomStateText.Text = result["cancelled"]?.GetValue<bool>() == true
                ? "Sala cancelada. O código não pode mais ser utilizado."
                : "A sala já não estava aguardando jogadores.";
            StatusText.Text = "Sala privada encerrada.";
        }
        catch (Exception ex) { StatusText.Text = "Falha ao cancelar a sala: " + ex.Message; }
        finally
        {
            CancelRoomButton.IsEnabled = false;
            SetModeButtons(!string.IsNullOrWhiteSpace(_token));
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _polling?.Cancel();
        if (!string.IsNullOrWhiteSpace(_token))
        {
            if (_queued)
            {
                try { await _api.PostAsync("/v1/queue/leave", new JsonObject()); }
                catch { /* Do not block window closing on network errors. */ }
            }
            if (_roomWaiting)
            {
                try { await _api.PostAsync("/v1/rooms/cancel", new JsonObject()); }
                catch { /* Do not block window closing on network errors. */ }
            }
        }
        _polling?.Dispose();
    }
}
