using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public partial class OnlineMatchWindow : Window
{
    private const string RankedEndpoint = "http://26.226.57.19:8765";
    private readonly RankedApiClient _api = new();
    private readonly RankedCredentialStore _credentials = new();
    private readonly OnlineGameSessionService _gameSession;
    private bool _guestSessionStarted;
    private bool _hostSessionActive;
    private CancellationTokenSource? _polling;
    private string? _token;
    private string? _playerId;
    private bool _queued;
    private bool _roomWaiting;
    private bool _matchActive;
    private string? _activeMatchId;
    private string? _matchStatus;

    public OnlineMatchWindow(string distributionRoot, string gameFolder, string pythonExe)
    {
        InitializeComponent();
        _gameSession = new OnlineGameSessionService(distributionRoot, gameFolder, pythonExe);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _api.Configure(RankedEndpoint, null);
        await RestoreSessionAsync();
    }

    private async Task RestoreSessionAsync()
    {
        try
        {
            var endpoint = RankedEndpoint;
            _token = _credentials.Read(endpoint);
            if (string.IsNullOrWhiteSpace(_token)) return;
            _api.Configure(endpoint, _token);
            ApplyProfile(await _api.GetAsync("/v1/me"));
            if (!await RestoreActiveMatchAsync())
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
            var endpoint = RankedEndpoint;
            if (string.IsNullOrWhiteSpace(NicknameBox.Text))
                throw new InvalidOperationException("Informe um nickname para criar ou identificar a conta.");
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
                StatusText.Text = "Conta criada e credencial protegida no Gerenciador de Credenciais do Windows.";
            }
            else
            {
                ApplyProfile(await _api.GetAsync("/v1/me"));
                if (!await RestoreActiveMatchAsync())
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

    private async Task<bool> RestoreActiveMatchAsync()
    {
        var result = await _api.GetAsync("/v1/match");
        if (result["match"] is not JsonObject match) return false;
        ShowMatch(match);
        return true;
    }

    private void ApplyProfile(JsonNode? profile)
    {
        if (profile is null) return;
        _playerId = profile["player_id"]?.GetValue<string>() ?? _playerId;
        var name = profile["display_name"]?.GetValue<string>() ?? NicknameBox.Text;
        var rating = profile["rating"]?.ToString() ?? "—";
        NicknameBox.Text = name;
        ProfileText.Text = $"{name}  •  Rating {rating}\nID: {_playerId ?? "—"}";
        SetModeButtons(!string.IsNullOrWhiteSpace(_token));
    }

    private void SetModeButtons(bool enabled)
    {
        SearchButton.IsEnabled = enabled && !_queued && !_roomWaiting && !_matchActive;
        CreateRoomButton.IsEnabled = enabled && !_queued && !_roomWaiting && !_matchActive;
        JoinRoomButton.IsEnabled = enabled && !_queued && !_roomWaiting && !_matchActive;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token) || _roomWaiting) return;
        SearchButton.IsEnabled = false;
        CreateRoomButton.IsEnabled = false;
        JoinRoomButton.IsEnabled = false;
        try
        {
            var response = await _api.PostAsync("/v1/queue/join", new JsonObject { ["region"] = "sa-east" });
            if (response["match"] is JsonObject immediate) { ShowMatch(immediate); return; }
            _queued = true;
            QueueStateText.Text = "PROCURANDO ADVERSÁRIO";
            QueueDetailText.Text = "Você entrou na fila South America. O servidor está procurando alguém com rating compatível.";
            CancelButton.IsEnabled = true;
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
                var matchResult = await _api.GetAsync("/v1/match", cancellationToken);
                if (matchResult["match"] is JsonObject match)
                {
                    await Dispatcher.InvokeAsync(() => ShowMatch(match));
                    return;
                }
                var queue = await _api.GetAsync("/v1/queue", cancellationToken);
                await Dispatcher.InvokeAsync(() =>
                {
                    var position = queue["position"]?.ToString() ?? "—";
                    QueueDetailText.Text = queue["queued"]?.GetValue<bool>() == true
                        ? $"Fila ativa • posição {position} • aguardando adversário."
                        : "O servidor não informa uma posição ativa na fila.";
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
            CopyRoomCodeButton.IsEnabled = true;
            RoomStateText.Text = "Sala aberta. Copie e compartilhe este código com seu adversário. Ela expira em 15 minutos.";
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
                        CopyRoomCodeButton.IsEnabled = false;
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

    private void CopyRoomCode_Click(object sender, RoutedEventArgs e)
    {
        var code = RoomCodeDisplayBox.Text.Trim();
        if (code.Length != 6 || code == "------") return;
        try
        {
            Clipboard.SetText(code);
            RoomStateText.Text = "Código copiado. Envie-o ao outro jogador.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível copiar o código: " + ex.Message;
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
        CancelRoomButton.IsEnabled = false;
        CopyRoomCodeButton.IsEnabled = false;
        _matchActive = true;
        _activeMatchId = match["match_id"]?.GetValue<string>();
        UpdateMatch(match);
        if (_matchActive) StartMatchLifecyclePolling();
    }

    private void UpdateMatch(JsonObject match)
    {
        var matchId = match["match_id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(_activeMatchId) &&
            !string.Equals(_activeMatchId, matchId, StringComparison.Ordinal)) return;
        _activeMatchId = matchId;
        _matchStatus = match["status"]?.GetValue<string>() ?? "assigned";
        _matchActive = _matchStatus is "assigned" or "ready";

        var opponent = match["opponent"]?["display_name"]?.GetValue<string>() ?? "Adversário";
        var rating = match["opponent"]?["rating"]?.ToString() ?? "—";
        var slot = match["player_slot"]?.ToString() ?? "—";
        var accepted = match["accepted"]?.GetValue<bool>() == true;
        var opponentAccepted = match["opponent_accepted"]?.GetValue<bool>() == true;
        MatchTitleText.Text = opponent;
        MatchDetailText.Text = $"Adversário: {opponent}\nRating: {rating}\nSua posição: Player {(slot == "0" ? "1" : slot == "1" ? "2" : slot)}\nPartida: {matchId ?? "—"}";

        var roomRole = match["room_role"]?.GetValue<string>();
        var roomStatus = match["room_status"]?.GetValue<string>();
        var connectionCode = match["connection_code"]?.GetValue<string>();
        if (roomRole == "guest" && roomStatus == "starting" &&
            !string.IsNullOrWhiteSpace(connectionCode) && !_guestSessionStarted)
        {
            _guestSessionStarted = true;
            _ = StartGuestSessionAsync(connectionCode);
        }
        StartRoomButton.Visibility = roomRole == "host" ? Visibility.Visible : Visibility.Collapsed;
        StartRoomButton.IsEnabled = roomRole == "host" &&
            (roomStatus == "matched" || (roomStatus == "starting" && !_hostSessionActive && string.IsNullOrWhiteSpace(connectionCode)));
        if (roomRole == "host" && roomStatus == "starting")
            StartRoomButton.Content = string.IsNullOrWhiteSpace(connectionCode) ? "TENTAR NOVAMENTE" : "AGUARDANDO CONVIDADO";
        if (_matchStatus == "ready" && roomRole is "host" or "guest")
        {
            QueueStateText.Text = roomStatus == "starting" ? "INÍCIO AUTORIZADO" : "SALA COMPLETA";
            MatchStateText.Text = roomStatus == "starting"
                ? "O anfitrião autorizou o início. A preparação da conexão P2P ainda precisa ser concluída."
                : roomRole == "host"
                    ? "O convidado entrou. Você está na sala automaticamente; clique em INICIAR PARTIDA quando estiver pronto."
                    : "Você entrou na sala. Aguarde o anfitrião clicar em INICIAR PARTIDA.";
            AcceptMatchButton.IsEnabled = false;
            DeclineMatchButton.IsEnabled = true;
            DeclineMatchButton.Content = "SAIR DA SALA";
            StatusText.Text = roomStatus == "starting"
                ? "Sinal de início recebido. Aguardando o código MR1 e preparando a sessão P2P."
                : roomRole == "host"
                    ? "Sala privada pronta. O anfitrião não precisa aceitar a própria sala."
                    : "Entrada confirmada. Aguardando o anfitrião.";
        }
        else if (_matchStatus == "ready")
        {
            QueueStateText.Text = "PARTIDA PRONTA";
            MatchStateText.Text = "Os dois jogadores aceitaram. A partida está pronta para a próxima etapa.";
            AcceptMatchButton.IsEnabled = false;
            DeclineMatchButton.IsEnabled = true;
            DeclineMatchButton.Content = "CANCELAR PARTIDA";
            StatusText.Text = "Aceite bilateral concluído. A sessão P2P ainda não foi iniciada.";
        }
        else
        {
            StartRoomButton.Visibility = Visibility.Collapsed;
            StartRoomButton.IsEnabled = false;
            QueueStateText.Text = "PARTIDA ENCONTRADA";
            MatchStateText.Text = accepted
                ? "Você aceitou. Aguardando a confirmação do adversário..."
                : opponentAccepted
                    ? "Seu adversário aceitou. Confirme para deixar a partida pronta."
                    : "A partida expira em 90 segundos se os dois jogadores não aceitarem.";
            AcceptMatchButton.IsEnabled = !accepted;
            DeclineMatchButton.IsEnabled = true;
            DeclineMatchButton.Content = "RECUSAR PARTIDA";
            StatusText.Text = accepted
                ? "Aceite enviado. Aguardando o outro jogador."
                : "Adversário encontrado. Aceite ou recuse a partida.";
        }

        RoomStateText.Text = "Partida atribuída pelo servidor.";
        QueueDetailText.Text = "O servidor atribuiu seu adversário.";
        SetModeButtons(false);
    }

    private void StartMatchLifecyclePolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = new CancellationTokenSource();
        _ = PollMatchLifecycleAsync(_activeMatchId, _polling.Token);
    }

    private async Task PollMatchLifecycleAsync(string? matchId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _matchActive &&
                   string.Equals(matchId, _activeMatchId, StringComparison.Ordinal))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                JsonObject result;
                try
                {
                    result = await _api.GetAsync("/v1/match", cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                        StatusText.Text = "Conexão instável ao atualizar a partida; tentando novamente: " + ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    continue;
                }

                if (result["match"] is not JsonObject current)
                {
                    await Dispatcher.InvokeAsync(() => ResetMatchState(
                        "A partida expirou, foi recusada ou deixou de estar ativa."));
                    return;
                }
                await Dispatcher.InvokeAsync(() => UpdateMatch(current));
                if (_matchStatus is not ("assigned" or "ready")) return;
            }
        }
        catch (OperationCanceledException) { }
    }

    private async void StartRoom_Click(object sender, RoutedEventArgs e)
    {
        if (!_matchActive || _matchStatus != "ready" || StartRoomButton.IsEnabled == false) return;
        StartRoomButton.IsEnabled = false;
        _hostSessionActive = true;
        try
        {
            await _api.PostAsync("/v1/rooms/start", new JsonObject());
            MatchStateText.Text = "Abrindo o jogo e preparando a sessão UDP pela Radmin...";
            StatusText.Text = "O jogo será iniciado nos dois PCs. Se aparecer a janela de configuração do Metal Revolution, pressione Play.";
            var code = await _gameSession.StartHostAsync();
            await _api.PostAsync("/v1/rooms/connection-code", new JsonObject { ["code"] = code });
            MatchStateText.Text = "Sessão do anfitrião iniciada. Aguardando o convidado conectar pela Radmin.";
            StatusText.Text = "Código de conexão autenticado enviado ao convidado. Aguardando handshake UDP e sincronização.";
            StartRoomButton.Content = "AGUARDANDO CONVIDADO";
            StartMatchLifecyclePolling();
            await _gameSession.WaitForPeerReadyAsync(TimeSpan.FromMinutes(9));
            MatchStateText.Text = "Conexão Radmin estabelecida. A seleção sincronizada está abrindo nos dois PCs.";
            StatusText.Text = "Handshake UDP autenticado e entrada VS confirmados. Escolham os personagens e confirmem Ready no jogo.";
        }
        catch (Exception ex)
        {
            _hostSessionActive = false;
            _gameSession.Stop();
            StartRoomButton.IsEnabled = true;
            StatusText.Text = "Não foi possível iniciar a sala: " + ex.Message;
        }
    }

    private async Task StartGuestSessionAsync(string connectionCode)
    {
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MatchStateText.Text = "O anfitrião iniciou. Abrindo o jogo e preparando a conexão UDP...";
                StatusText.Text = "Se aparecer a janela de configuração do Metal Revolution, pressione Play.";
            });
            await _gameSession.StartGuestAsync(connectionCode);
            await _gameSession.WaitForPeerReadyAsync(TimeSpan.FromMinutes(9));
            await Dispatcher.InvokeAsync(() =>
            {
                MatchStateText.Text = "Conexão Radmin estabelecida. A seleção sincronizada está abrindo nos dois PCs.";
                StatusText.Text = "Handshake UDP autenticado e entrada VS confirmados. Escolham os personagens e confirmem Ready no jogo.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MatchStateText.Text = "Não foi possível iniciar o cliente P2P.";
                StatusText.Text = "Falha ao preparar o jogo/conexão: " + ex.Message;
            });
        }
    }

    private async void AcceptMatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_matchActive || _matchStatus != "assigned") return;
        AcceptMatchButton.IsEnabled = false;
        try
        {
            var result = await _api.PostAsync("/v1/match/accept", new JsonObject());
            if (result["match"] is JsonObject match)
            {
                UpdateMatch(match);
                if (_matchActive) StartMatchLifecyclePolling();
            }
        }
        catch (Exception ex)
        {
            AcceptMatchButton.IsEnabled = true;
            StatusText.Text = "Não foi possível aceitar a partida: " + ex.Message;
        }
    }

    private async void DeclineMatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_matchActive) return;
        DeclineMatchButton.IsEnabled = false;
        try
        {
            await _api.PostAsync("/v1/match/decline", new JsonObject());
            ResetMatchState("Partida recusada/cancelada. Você já pode procurar outro adversário.");
        }
        catch (Exception ex)
        {
            DeclineMatchButton.IsEnabled = true;
            StatusText.Text = "Não foi possível recusar/cancelar a partida: " + ex.Message;
        }
    }

    private void ResetMatchState(string message)
    {
        _polling?.Cancel();
        _matchActive = false;
        _activeMatchId = null;
        _matchStatus = null;
        _guestSessionStarted = false;
        _hostSessionActive = false;
        StartRoomButton.Content = "INICIAR PARTIDA";
        MatchTitleText.Text = "Aguardando adversário";
        MatchDetailText.Text = "Quando o servidor encontrar um adversário, os dados da partida aparecerão aqui.";
        MatchStateText.Text = message;
        AcceptMatchButton.IsEnabled = false;
        StartRoomButton.IsEnabled = false;
        StartRoomButton.Visibility = Visibility.Collapsed;
        DeclineMatchButton.IsEnabled = false;
        DeclineMatchButton.Content = "RECUSAR PARTIDA";
        QueueStateText.Text = "PRONTO PARA BUSCAR";
        StatusText.Text = message;
        SetModeButtons(!string.IsNullOrWhiteSpace(_token));
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _polling?.Cancel();
        try
        {
            await _api.PostAsync("/v1/queue/leave", new JsonObject());
            _queued = false;
            QueueStateText.Text = "BUSCA CANCELADA";
            QueueDetailText.Text = "Você saiu da fila.";
            StatusText.Text = "Busca cancelada.";
        }
        catch (Exception ex) { StatusText.Text = "Falha ao cancelar a busca: " + ex.Message; }
        finally
        {
            CancelButton.IsEnabled = false;
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
            CopyRoomCodeButton.IsEnabled = false;
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
            if (_matchActive)
            {
                try { await _api.PostAsync("/v1/match/decline", new JsonObject()); }
                catch { /* Do not block window closing on network errors. */ }
            }
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
        _gameSession.Dispose();
    }
}
