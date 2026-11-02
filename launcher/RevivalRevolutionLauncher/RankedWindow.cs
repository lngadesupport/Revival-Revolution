using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public sealed class RankedWindow : Window
{
    private readonly RankedCredentialStore _credentials = new();
    private readonly TextBox _serverUrl = new();
    private readonly TextBlock _machineName = new();
    private readonly TextBlock _identityStatus = new();
    private readonly TextBlock _rating = new();
    private readonly TextBlock _queueStatus = new();
    private readonly TextBlock _matchStatus = new();
    private readonly Button _connectButton = new();
    private readonly Button _queueButton = new();
    private readonly Button _leaveButton = new();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private RankedCredentialStore.Credentials? _saved;
    private RankedProfile? _profile;
    private RankedApiClient? _client;
    private bool _polling;
    private bool _busy;
    private bool _matchAssigned;
    private bool _isQueued;

    public RankedWindow()
    {
        Title = "Ranked Match — Revival Revolution";
        Width = 650;
        Height = 620;
        MinWidth = 580;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#090D18");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        _saved = _credentials.Load();
        var savedUrl = _saved?.ServerUrl ?? _credentials.LoadServerUrl();
        _serverUrl.Text = savedUrl;
        _serverUrl.IsReadOnly = _saved is not null;
        _machineName.Text = Environment.MachineName;
        _connectButton.Content = _saved is null ? "CRIAR IDENTIDADE / CONECTAR" : "CONECTAR";
        _queueButton.Content = "BUSCAR PARTIDA RANQUEADA";
        _leaveButton.Content = "CANCELAR BUSCA";
        _queueButton.IsEnabled = false;
        _leaveButton.IsEnabled = false;

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock
        {
            Text = "RANKED MATCH",
            FontSize = 29,
            FontWeight = FontWeights.Black,
            Foreground = Brush("#FFF0F2"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Encontre um adversário automaticamente no servidor da comunidade.",
            FontSize = 12,
            Foreground = Brush("#B8BECE"),
            Margin = new Thickness(0, 0, 0, 20)
        });

        root.Children.Add(SectionLabel("SERVIDOR"));
        root.Children.Add(_serverUrl);
        _serverUrl.Margin = new Thickness(0, 5, 0, 10);
        _serverUrl.Padding = new Thickness(10, 9, 10, 9);
        _serverUrl.Background = Brush("#151B2B");
        _serverUrl.Foreground = Brushes.White;
        _serverUrl.BorderBrush = Brush("#46516A");
        _serverUrl.CaretBrush = Brushes.White;
        _serverUrl.FontSize = 13;
        _serverUrl.ToolTip = "URL HTTPS da API Ranked hospedada por você. HTTP só é aceito em localhost para testes.";

        root.Children.Add(SectionLabel("NOME PÚBLICO DO JOGADOR"));
        root.Children.Add(ValuePanel(_machineName, "Este nome vem automaticamente do Windows (MachineName)."));
        root.Children.Add(SectionLabel("IDENTIDADE E RATING"));
        root.Children.Add(ValuePanel(_identityStatus, "A identidade competitiva é um UUID do servidor; o nome do PC é apenas o rótulo."));
        root.Children.Add(ValuePanel(_rating, "Rating inicial definido pelo servidor."));
        _identityStatus.Text = _saved is null ? "IDENTIDADE AINDA NÃO CRIADA" : "CREDENCIAL LOCAL ENCONTRADA";
        _rating.Text = "Rating: —";

        _connectButton.Click += Connect_Click;
        StyleButton(_connectButton, primary: true);
        root.Children.Add(_connectButton);

        root.Children.Add(SectionLabel("MATCHMAKING"));
        root.Children.Add(ValuePanel(_queueStatus, "Fila 1v1 por região e proximidade de rating."));
        _queueStatus.Text = "Não conectado à fila.";
        _queueButton.Click += JoinQueue_Click;
        _leaveButton.Click += LeaveQueue_Click;
        StyleButton(_queueButton, primary: true);
        StyleButton(_leaveButton, primary: false);
        root.Children.Add(_queueButton);
        root.Children.Add(_leaveButton);

        root.Children.Add(SectionLabel("PARTIDA"));
        root.Children.Add(ValuePanel(_matchStatus,
            "Quando um adversário for encontrado, o launcher mostrará os dados da atribuição."));
        _matchStatus.Text = "Nenhuma partida atribuída.";

        root.Children.Add(new TextBlock
        {
            Text = "A sessão P2P ainda não é iniciada automaticamente. O ticket de autorização e a integração com peer_session.py serão ativados somente depois da validação do protocolo e da bridge.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Foreground = Brush("#E8B77C"),
            Margin = new Thickness(0, 18, 0, 0)
        });

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brush("#090D18")
        };

        _pollTimer.Tick += async (_, _) => await PollAsync();
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            _client?.Dispose();
        };
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var url = _serverUrl.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)))
        {
            ShowError("Informe a URL HTTPS da API Ranked. HTTP só é permitido para localhost.");
            return;
        }

        if (_saved is not null && !string.Equals(url, _saved.ServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Esta credencial pertence ao servidor salvo. Não alterei nem descartei sua identidade. Para trocar de servidor, será necessário um fluxo explícito de migração/reset.");
            return;
        }

        SetBusy(true);
        try
        {
            if (_saved is not null)
            {
                _client?.Dispose();
                _client = new RankedApiClient(url, _saved.Token);
                _profile = await _client.GetProfileAsync();
                _identityStatus.Text = $"Conectado • {_profile.DisplayName} • ID {_profile.PlayerId[..8]}…";
            }
            else
            {
                _credentials.SaveServerUrl(url);
                // Register once, then immediately protect the returned token with DPAPI.
                _saved = await RegisterAndSaveAsync(url);
                _client?.Dispose();
                _client = new RankedApiClient(url, _saved.Token);
                _identityStatus.Text = $"Identidade criada • {_profile.DisplayName} • ID {_profile.PlayerId[..8]}…";
                _serverUrl.IsReadOnly = true;
                _connectButton.Content = "ATUALIZAR PERFIL";
            }

            if (_profile is null) throw new InvalidDataException("Perfil não retornado pelo servidor.");
            _rating.Text = $"Rating: {_profile.Rating}";
            _queueButton.IsEnabled = true;
            _identityStatus.Foreground = Brush("#85E3A4");
            _queueStatus.Text = "Pronto para entrar na fila.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally { SetBusy(false); }
    }

    private async Task<RankedCredentialStore.Credentials> RegisterAndSaveAsync(string url)
    {
        // Registration returns the bearer token. This request is the only place
        // where the plaintext token exists before it is protected with Windows DPAPI.
        using var client = new RankedRegistrationClient(url);
        var result = await client.RegisterAndSaveAsync(Environment.MachineName, _credentials);
        return result;
    }

    private async void JoinQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _client is null || _profile is null || _matchAssigned) return;
        SetBusy(true);
        try
        {
            var response = await _client.JoinQueueAsync("global");
            if (response.TryGetProperty("match", out var match) && match.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                await ShowMatchAsync();
                return;
            }
            _queueStatus.Text = "Na fila • procurando adversário…";
            _isQueued = true;
            _queueButton.IsEnabled = false;
            _leaveButton.IsEnabled = true;
            _pollTimer.Start();
            await PollAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private async void LeaveQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _client is null) return;
        SetBusy(true);
        try
        {
            var left = await _client.LeaveQueueAsync();
            _pollTimer.Stop();
            _isQueued = false;
            _queueStatus.Text = left ? "Busca cancelada." : "Você não estava na fila.";
            _queueButton.IsEnabled = true;
            _leaveButton.IsEnabled = false;
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private async Task PollAsync()
    {
        if (_polling || _client is null || _matchAssigned || !IsVisible) return;
        _polling = true;
        try
        {
            var match = await _client.GetMatchAsync();
            if (match is not null)
            {
                await ShowMatchAsync(match);
                return;
            }
            var queue = await _client.GetQueueAsync();
            if (queue.TryGetProperty("queued", out var queued) && queued.GetBoolean())
            {
                var position = queue.TryGetProperty("position", out var pos) ? pos.GetInt32() : 0;
                _queueStatus.Text = $"Na fila • posição {position} • região {queue.GetProperty("region").GetString()}";
            }
            else
            {
                _queueStatus.Text = "Não há busca ativa.";
                _isQueued = false;
                _pollTimer.Stop();
                _queueButton.IsEnabled = true;
                _leaveButton.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            _queueStatus.Text = "Falha temporária ao consultar a fila: " + ex.Message;
        }
        finally { _polling = false; }
    }

    private async Task ShowMatchAsync(RankedMatch? match = null)
    {
        match ??= await _client!.GetMatchAsync();
        if (match is null) return;
        _matchAssigned = true;
        _isQueued = false;
        _pollTimer.Stop();
        _queueStatus.Text = "Adversário encontrado.";
        _matchStatus.Text = $"Partida: {match.MatchId}\nAdversário: {match.OpponentName}\nRating: {match.OpponentRating}\nSeu slot: {match.PlayerSlot}\nRegião: {match.Region}\nEstado: {match.Status}\n\nAtribuição recebida. A abertura do jogo aguarda ticket P2P seguro.";
        _queueButton.IsEnabled = false;
        _leaveButton.IsEnabled = false;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = Brush("#FF7586"),
        Margin = new Thickness(0, 12, 0, 2)
    };

    private static Border ValuePanel(TextBlock text, string tooltip)
    {
        text.TextWrapping = TextWrapping.Wrap;
        text.FontSize = 13;
        text.Foreground = Brush("#F0F1F6");
        text.Margin = new Thickness(0, 2, 0, 2);
        return new Border
        {
            Background = Brush("#151B2B"),
            BorderBrush = Brush("#344057"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 9, 11, 9),
            Margin = new Thickness(0, 4, 0, 4),
            Child = new StackPanel { Children = { text, new TextBlock { Text = tooltip, TextWrapping = TextWrapping.Wrap, FontSize = 9, Foreground = Brush("#9DA7BB"), Margin = new Thickness(0, 3, 0, 0) } } }
        };
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private static void StyleButton(Button button, bool primary)
    {
        button.Margin = new Thickness(0, 8, 0, 4);
        button.Padding = new Thickness(12, 11, 12, 11);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.FontWeight = FontWeights.Bold;
        button.Foreground = Brushes.White;
        button.Background = Brush(primary ? "#A92335" : "#293247");
        button.BorderBrush = Brush(primary ? "#E85A6C" : "#52617C");
        button.BorderThickness = new Thickness(1);
        button.Cursor = System.Windows.Input.Cursors.Hand;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _connectButton.IsEnabled = !busy;
        _queueButton.IsEnabled = !busy && _profile is not null && !_matchAssigned && !_isQueued;
        _leaveButton.IsEnabled = !busy && _isQueued;
    }

    private void ShowError(string message)
    {
        _identityStatus.Foreground = Brush("#FF8A92");
        MessageBox.Show(this, message, "Ranked Match", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

/// <summary>
/// Keeps the registration token handling in one narrow path so it can be
/// immediately encrypted by DPAPI and never written to logs.
/// </summary>
internal sealed class RankedRegistrationClient(string serverUrl) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<RankedCredentialStore.Credentials> RegisterAndSaveAsync(
        string displayName, RankedCredentialStore store)
    {
        var uri = new Uri(serverUrl.TrimEnd('/') + "/v1/register");
        using var response = await _http.PostAsJsonAsync(uri, new { display_name = displayName });
        var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(body?.message ?? $"Falha no cadastro: {(int)response.StatusCode}");
        if (body is null || string.IsNullOrWhiteSpace(body.player_id) || string.IsNullOrWhiteSpace(body.token))
            throw new InvalidDataException("O servidor não retornou a identidade e o token.");
        store.Save(serverUrl, body.player_id, body.display_name ?? displayName, body.token);
        return store.Load() ?? throw new InvalidDataException("Não foi possível reabrir a credencial protegida pelo Windows.");
    }

    private sealed class RegistrationResponse
    {
        public string? player_id { get; set; }
        public string? display_name { get; set; }
        public string? token { get; set; }
        public string? message { get; set; }
    }

    public void Dispose() => _http.Dispose();
}
