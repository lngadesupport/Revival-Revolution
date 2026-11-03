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
            StatusText.Text = "Conta autenticada. Você já pode entrar na fila.";
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
                StatusText.Text = "Conta criada e credencial protegida no Gerenciador de Credenciais do Windows.";
            }
            else
            {
                ApplyProfile(await _api.GetAsync("/v1/me"));
                StatusText.Text = "Sessão restaurada com segurança.";
            }
            SearchButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao conectar: " + ex.Message;
            ProfileText.Text = "Conta não conectada.";
            SearchButton.IsEnabled = false;
        }
        finally { ConnectButton.IsEnabled = true; }
    }

    private void ApplyProfile(JsonNode? profile)
    {
        if (profile is null) return;
        _playerId = profile["player_id"]?.GetValue<string>() ?? _playerId;
        var name = profile["display_name"]?.GetValue<string>() ?? NicknameBox.Text;
        var rating = profile["rating"]?.ToString() ?? "—";
        NicknameBox.Text = name;
        ProfileText.Text = $"{name}  •  Rating {rating}\nID: {_playerId ?? "—"}";
        SearchButton.IsEnabled = !string.IsNullOrWhiteSpace(_token);
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token)) return;
        SearchButton.IsEnabled = false;
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
            SearchButton.IsEnabled = true;
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
                StatusText.Text = "A consulta da fila falhou: " + ex.Message;
                SearchButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            });
        }
    }

    private void ShowMatch(JsonObject match)
    {
        _queued = false;
        _polling?.Cancel();
        CancelButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        QueueStateText.Text = "PARTIDA ENCONTRADA";
        var opponent = match["opponent"]?["display_name"]?.GetValue<string>() ?? "Adversário";
        var rating = match["opponent"]?["rating"]?.ToString() ?? "—";
        var matchId = match["match_id"]?.GetValue<string>() ?? "—";
        var slot = match["player_slot"]?.ToString() ?? "—";
        MatchTitleText.Text = opponent;
        MatchDetailText.Text = $"Adversário: {opponent}\nRating: {rating}\nSua posição: Player {(slot == "0" ? "1" : slot == "1" ? "2" : slot)}\nPartida: {matchId}";
        QueueDetailText.Text = "O servidor atribuiu seu adversário.";
        StatusText.Text = "Matchmaking concluído. A sessão P2P ainda não foi iniciada: falta emitir e validar o ticket de conexão.";
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
            SearchButton.IsEnabled = !string.IsNullOrWhiteSpace(_token);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _polling?.Cancel();
        if (_queued && !string.IsNullOrWhiteSpace(_token))
        {
            try { await _api.PostAsync("/v1/queue/leave", new JsonObject()); }
            catch { /* Do not block window closing on network errors. */ }
        }
        _polling?.Dispose();
    }
}