using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public partial class OnlineMatchWindow : Window
{
    private const string RankedEndpoint = "http://26.226.57.19:8765";
    private readonly RankedApiClient _api = new();
    private readonly RankedCredentialStore _credentials = new();
    private readonly OnlineGameSessionService _gameSession;
    private readonly CancellationTokenSource _sessionCancellation = new();
    private CancellationTokenSource? _polling;
    private string? _token;
    private string? _playerId;
    private string? _activeMatchId;
    private string? _matchStatus;
    private string? _opponentId;
    private bool _roomWaiting;
    private bool _matchActive;
    private bool _guestSessionStarted;
    private bool _hostSessionActive;
    private bool _closing;

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
            _token = _credentials.Read(RankedEndpoint);
            if (string.IsNullOrWhiteSpace(_token))
            {
                StatusText.Text = "Conecte-se ou crie sua conta para criar e entrar em salas.";
                return;
            }

            _api.Configure(RankedEndpoint, _token);
            ApplyProfile(await _api.GetAsync("/v1/me"));
            if (await RestoreActiveMatchAsync()) return;
            await RestoreOpenRoomAsync();
            StatusText.Text = _roomWaiting
                ? "Convite restaurado. Aguardando o convidado entrar."
                : "Conta conectada. Crie uma sala ou insira o código de um convite.";
        }
        catch (Exception ex)
        {
            _token = null;
            ProfileText.Text = "Não foi possível restaurar a sessão.";
            StatusText.Text = "Falha ao conectar ao servidor: " + ex.Message;
            SetModeButtons(false);
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            _token = _credentials.Read(RankedEndpoint);
            _api.Configure(RankedEndpoint, _token);
            if (string.IsNullOrWhiteSpace(_token))
            {
                if (string.IsNullOrWhiteSpace(NicknameBox.Text))
                    throw new InvalidOperationException("Informe um nickname para criar sua conta.");
                var created = await _api.PostAsync("/v1/register", new JsonObject
                {
                    ["display_name"] = NicknameBox.Text.Trim()
                }, authenticated: false);
                _token = created["token"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("O servidor não devolveu o token da conta.");
                _credentials.Write(RankedEndpoint, _token);
                _api.Configure(RankedEndpoint, _token);
                ApplyProfile(await _api.GetAsync("/v1/me"));
                StatusText.Text = "Conta criada. Sua credencial foi protegida no Gerenciador de Credenciais do Windows.";
            }
            else
            {
                ApplyProfile(await _api.GetAsync("/v1/me"));
                if (!await RestoreActiveMatchAsync())
                    await RestoreOpenRoomAsync();
                StatusText.Text = "Conta conectada.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao conectar: " + ex.Message;
            ProfileText.Text = "Conta não conectada.";
            SetModeButtons(false);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void ApplyProfile(JsonNode? profile)
    {
        if (profile is null) return;
        _playerId = profile["player_id"]?.GetValue<string>() ?? _playerId;
        var name = profile["display_name"]?.GetValue<string>() ?? NicknameBox.Text;
        NicknameBox.Text = name;
        MyNicknameText.Text = name;
        ProfileText.Text = "CONTA CONECTADA";
        AvatarButton.IsEnabled = !string.IsNullOrWhiteSpace(_token);
        var avatar = profile["avatar_data"]?.GetValue<string>() ?? string.Empty;
        SetOwnAvatar(avatar, name);
        SetModeButtons(!string.IsNullOrWhiteSpace(_token));
    }

    private void SetModeButtons(bool enabled)
    {
        CreateRoomButton.IsEnabled = enabled && !_roomWaiting && !_matchActive;
        JoinRoomButton.IsEnabled = enabled && !_roomWaiting && !_matchActive;
        RoomCodeBox.IsEnabled = enabled && !_roomWaiting && !_matchActive;
    }

    private async Task<bool> RestoreActiveMatchAsync()
    {
        var result = await _api.GetAsync("/v1/match");
        if (result["match"] is not JsonObject match) return false;
        ShowMatch(match);
        return true;
    }

    private async Task RestoreOpenRoomAsync()
    {
        var result = await _api.GetAsync("/v1/rooms/current");
        if (result["room"] is not JsonObject room ||
            room["status"]?.GetValue<string>() != "waiting") return;

        var code = room["room_code"]?.GetValue<string>() ?? string.Empty;
        if (code.Length != 6) return;
        _roomWaiting = true;
        RoomCodeDisplayBox.Text = code;
        CreateRoomButton.IsEnabled = false;
        CopyRoomCodeButton.IsEnabled = true;
        CancelRoomButton.Visibility = Visibility.Visible;
        CancelRoomButton.IsEnabled = true;
        RoomCodeBox.IsEnabled = false;
        JoinRoomButton.IsEnabled = false;
        StatusText.Text = "Convite restaurado. O código continua ativo.";
        _polling?.Cancel();
        _polling = new CancellationTokenSource();
        _ = PollRoomAsync(_polling.Token);
    }

    private async void CreateRoom_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token) || _roomWaiting || _matchActive) return;
        CreateRoomButton.IsEnabled = false;
        JoinRoomButton.IsEnabled = false;
        try
        {
            var result = await _api.PostAsync("/v1/rooms/create", new JsonObject { ["region"] = "sa-east" });
            var room = result["room"] as JsonObject
                ?? throw new InvalidOperationException("O servidor não devolveu os dados da sala.");
            var code = room["room_code"]?.GetValue<string>()
                ?? throw new InvalidOperationException("O servidor não devolveu o código da sala.");

            _roomWaiting = true;
            RoomCodeDisplayBox.Text = code;
            CopyRoomCodeButton.IsEnabled = true;
            CancelRoomButton.Visibility = Visibility.Visible;
            CancelRoomButton.IsEnabled = true;
            RoomCodeBox.IsEnabled = false;
            StatusText.Text = "Convite criado. Copie o código e envie ao seu adversário.";
            _polling?.Cancel();
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
                    await Dispatcher.InvokeAsync(() => ResetToInvite("O convite expirou ou foi encerrado."));
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_closing) return;
            await Dispatcher.InvokeAsync(() =>
                StatusText.Text = "Não foi possível atualizar o convite. Tentando novamente: " + ex.Message);
            try { await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken); }
            catch (OperationCanceledException) { }
            if (!cancellationToken.IsCancellationRequested && _roomWaiting)
                _ = PollRoomAsync(cancellationToken);
        }
    }

    private void CopyRoomCode_Click(object sender, RoutedEventArgs e)
    {
        var code = RoomCodeDisplayBox.Text.Trim();
        if (code.Length != 6 || code == "------") return;
        try
        {
            Clipboard.SetText(code);
            StatusText.Text = "Código copiado. Envie-o ao seu adversário.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível copiar o código: " + ex.Message;
        }
    }

    private async void JoinRoom_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token) || _roomWaiting || _matchActive) return;
        var code = RoomCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            StatusText.Text = "Digite o código de seis caracteres recebido do anfitrião.";
            return;
        }

        JoinRoomButton.IsEnabled = false;
        try
        {
            var result = await _api.PostAsync("/v1/rooms/join", new JsonObject { ["code"] = code });
            var match = result["match"] as JsonObject
                ?? throw new InvalidOperationException("A sala não devolveu os dados da partida.");
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
        _roomWaiting = false;
        _matchActive = true;
        _activeMatchId = match["match_id"]?.GetValue<string>();
        InvitePanel.Visibility = Visibility.Collapsed;
        VersusPanel.Visibility = Visibility.Visible;
        _polling?.Cancel();
        CancelRoomButton.IsEnabled = false;
        UpdateMatch(match);
        if (_matchActive) StartMatchLifecyclePolling();
    }

    private void UpdateMatch(JsonObject match)
    {
        var matchId = match["match_id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(_activeMatchId) &&
            !string.Equals(_activeMatchId, matchId, StringComparison.Ordinal)) return;

        _activeMatchId = matchId;
        _matchStatus = match["status"]?.GetValue<string>() ?? "ready";
        _matchActive = _matchStatus is "assigned" or "ready";
        var opponent = match["opponent"] as JsonObject;
        var opponentName = opponent?["display_name"]?.GetValue<string>() ?? "Adversário";
        var opponentId = opponent?["player_id"]?.GetValue<string>();
        MyVersusNicknameText.Text = MyNicknameText.Text;
        OpponentNicknameText.Text = opponentName;
        OpponentRoleText.Text = "CONVIDADO";
        if (!string.Equals(_opponentId, opponentId, StringComparison.Ordinal))
        {
            _opponentId = opponentId;
            SetAvatar(OpponentAvatarCircle, OpponentAvatarInitials, string.Empty, opponentName);
            if (!string.IsNullOrWhiteSpace(opponentId))
                _ = LoadOpponentAvatarAsync(opponentId, opponentName);
        }

        var role = match["room_role"]?.GetValue<string>();
        var roomStatus = match["room_status"]?.GetValue<string>();
        var connectionCode = match["connection_code"]?.GetValue<string>();
        if (role == "guest") OpponentRoleText.Text = "ANFITRIÃO";

        if (role == "guest" && roomStatus == "starting" &&
            !string.IsNullOrWhiteSpace(connectionCode) && !_guestSessionStarted)
        {
            _guestSessionStarted = true;
            _ = StartGuestSessionAsync(connectionCode);
        }

        StartRoomButton.Visibility = role == "host" ? Visibility.Visible : Visibility.Collapsed;
        StartRoomButton.IsEnabled = role == "host" && !_hostSessionActive &&
            (roomStatus == "matched" || (roomStatus == "starting" && string.IsNullOrWhiteSpace(connectionCode)));
        if (role == "host" && roomStatus == "starting")
            StartRoomButton.Content = "TENTAR NOVAMENTE";
        else
            StartRoomButton.Content = "INICIAR PARTIDA";

        if (_matchStatus == "ready" && role is "host" or "guest")
        {
            VersusStateText.Text = roomStatus == "starting"
                ? (role == "host" ? "Preparando o jogo e aguardando o adversário conectar..." : "O anfitrião iniciou a partida. Preparando sua conexão...")
                : (role == "host" ? "Seu adversário entrou. Quando estiver pronto, inicie a partida." : "Você entrou na sala. Aguarde o anfitrião iniciar a partida.");
            LeaveRoomButton.Content = "SAIR DA SALA";
            LeaveRoomButton.IsEnabled = true;
            StatusText.Text = roomStatus == "starting"
                ? "Inicialização em andamento pela rede privada Radmin."
                : "Os dois jogadores estão na sala VS.";
        }
        else
        {
            VersusStateText.Text = "A sala não está mais disponível.";
            StartRoomButton.IsEnabled = false;
            LeaveRoomButton.IsEnabled = false;
            StatusText.Text = "A partida expirou ou foi encerrada.";
        }
        SetModeButtons(false);
    }

    private async Task LoadOpponentAvatarAsync(string playerId, string nickname)
    {
        try
        {
            var result = await _api.GetAsync($"/v1/players/{playerId}/avatar");
            var avatar = result["avatar_data"]?.GetValue<string>() ?? string.Empty;
            await Dispatcher.InvokeAsync(() =>
            {
                if (string.Equals(_opponentId, playerId, StringComparison.Ordinal))
                    SetAvatar(OpponentAvatarCircle, OpponentAvatarInitials, avatar, nickname);
            });
        }
        catch
        {
            // A missing photo is represented by the initials fallback.
        }
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
                    await Dispatcher.InvokeAsync(() => StatusText.Text = "Conexão instável. Tentando atualizar a sala: " + ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
                    continue;
                }
                if (result["match"] is not JsonObject current)
                {
                    await Dispatcher.InvokeAsync(() => ResetToInvite("A partida foi encerrada ou expirou."));
                    return;
                }
                await Dispatcher.InvokeAsync(() => UpdateMatch(current));
                if (_matchStatus is not ("assigned" or "ready")) return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closing)
                await Dispatcher.InvokeAsync(() => StatusText.Text = "Reconectando à sala: " + ex.Message);
        }
    }

    private async void StartRoom_Click(object sender, RoutedEventArgs e)
    {
        if (!_matchActive || _matchStatus != "ready" || !StartRoomButton.IsEnabled) return;
        StartRoomButton.IsEnabled = false;
        _hostSessionActive = true;
        var codePublished = false;
        try
        {
            await _api.PostAsync("/v1/rooms/start", new JsonObject());
            await Dispatcher.InvokeAsync(() =>
            {
                StartRoomButton.Content = "CONECTANDO...";
                VersusStateText.Text = "Abrindo o jogo diretamente e preparando a conexão com o adversário...";
                StatusText.Text = "O jogo será iniciado sem a janela Metal Revolution Configuration.";
            });

            var connectionCode = await _gameSession.StartHostAsync(_sessionCancellation.Token);
            await _api.PostAsync("/v1/rooms/connection-code", new JsonObject { ["code"] = connectionCode });
            codePublished = true;
            await Dispatcher.InvokeAsync(() =>
            {
                StartRoomButton.Content = "AGUARDANDO ADVERSÁRIO";
                VersusStateText.Text = "Jogo aberto. Aguardando a conexão do convidado...";
            });
            StartMatchLifecyclePolling();
            await _gameSession.WaitForPeerReadyAsync(TimeSpan.FromMinutes(9), _sessionCancellation.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                VersusStateText.Text = "Conexão estabelecida! Os dois jogadores estão prontos para o combate.";
                StartRoomButton.Content = "PARTIDA INICIADA";
            });
        }
        catch (Exception ex)
        {
            _hostSessionActive = false;
            _gameSession.Stop();
            if (_closing) return;
            if (codePublished)
            {
                try { await _api.PostAsync("/v1/match/decline", new JsonObject()); } catch { }
                await Dispatcher.InvokeAsync(() => ResetToInvite("A conexão falhou. Crie uma nova sala. " + ex.Message));
            }
            else
            {
                StartRoomButton.IsEnabled = true;
                StartRoomButton.Content = "TENTAR NOVAMENTE";
                StatusText.Text = "Não foi possível iniciar o jogo: " + ex.Message;
            }
        }
    }

    private async Task StartGuestSessionAsync(string connectionCode)
    {
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                VersusStateText.Text = "O anfitrião iniciou. Abrindo o jogo diretamente...";
                StatusText.Text = "Preparando o jogo e a conexão P2P pela Radmin.";
            });
            await _gameSession.StartGuestAsync(connectionCode, _sessionCancellation.Token);
            await _gameSession.WaitForPeerReadyAsync(TimeSpan.FromMinutes(9), _sessionCancellation.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                VersusStateText.Text = "Conexão estabelecida! Os dois jogadores estão prontos para o combate.";
                StartRoomButton.Content = "PARTIDA INICIADA";
            });
        }
        catch (Exception ex)
        {
            _gameSession.Stop();
            if (_closing) return;
            await Dispatcher.InvokeAsync(() =>
            {
                VersusStateText.Text = "Não foi possível abrir a partida.";
                StatusText.Text = "Falha ao preparar o jogo/conexão: " + ex.Message;
            });
        }
    }

    private async void DeclineMatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_matchActive) return;
        LeaveRoomButton.IsEnabled = false;
        try
        {
            await _api.PostAsync("/v1/match/decline", new JsonObject());
            _gameSession.Stop();
            ResetToInvite("Você saiu da sala. Já pode criar ou entrar em outro convite.");
        }
        catch (Exception ex)
        {
            LeaveRoomButton.IsEnabled = true;
            StatusText.Text = "Não foi possível sair da sala: " + ex.Message;
        }
    }

    private async void CancelRoom_Click(object sender, RoutedEventArgs e)
    {
        _polling?.Cancel();
        try
        {
            await _api.PostAsync("/v1/rooms/cancel", new JsonObject());
            _roomWaiting = false;
            ResetToInvite("Convite cancelado. O código não pode mais ser utilizado.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao cancelar o convite: " + ex.Message;
            if (_roomWaiting) _polling = new CancellationTokenSource();
        }
    }

    private void ResetToInvite(string message)
    {
        _polling?.Cancel();
        _roomWaiting = false;
        _matchActive = false;
        _activeMatchId = null;
        _matchStatus = null;
        _opponentId = null;
        _guestSessionStarted = false;
        _hostSessionActive = false;
        InvitePanel.Visibility = Visibility.Visible;
        VersusPanel.Visibility = Visibility.Collapsed;
        RoomCodeDisplayBox.Text = "------";
        RoomCodeBox.Clear();
        RoomCodeBox.IsEnabled = true;
        CopyRoomCodeButton.IsEnabled = false;
        CancelRoomButton.IsEnabled = false;
        CancelRoomButton.Visibility = Visibility.Collapsed;
        StartRoomButton.Content = "INICIAR PARTIDA";
        StartRoomButton.IsEnabled = false;
        StartRoomButton.Visibility = Visibility.Collapsed;
        LeaveRoomButton.IsEnabled = false;
        SetModeButtons(!string.IsNullOrWhiteSpace(_token));
        StatusText.Text = message;
    }

    private async void Avatar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_token)) return;
        var dialog = new OpenFileDialog
        {
            Title = "Escolha sua foto de perfil",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.webp",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            if (bytes.Length == 0 || bytes.Length > 256 * 1024)
                throw new InvalidOperationException("A foto precisa ter até 256 KiB.");
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var mime = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => throw new InvalidOperationException("Formato de imagem não suportado.")
            };
            var data = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            var updated = await _api.PostAsync("/v1/profile", new JsonObject { ["avatar_data"] = data });
            ApplyProfile(updated);
            StatusText.Text = "Foto de perfil atualizada.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível atualizar a foto: " + ex.Message;
        }
    }

    private void SetOwnAvatar(string avatarData, string nickname)
    {
        SetAvatar(MyAvatarCircle, MyAvatarInitials, avatarData, nickname);
        SetAvatar(MyVersusAvatarCircle, MyVersusAvatarInitials, avatarData, nickname);
    }

    private static void SetAvatar(System.Windows.Shapes.Ellipse circle, System.Windows.Controls.TextBlock initials,
        string avatarData, string nickname)
    {
        var text = GetInitials(nickname);
        initials.Text = text;
        initials.Visibility = Visibility.Visible;
        circle.Fill = new SolidColorBrush(Color.FromRgb(52, 57, 77));
        if (string.IsNullOrWhiteSpace(avatarData)) return;

        try
        {
            var comma = avatarData.IndexOf(',');
            if (comma < 0) return;
            var bytes = Convert.FromBase64String(avatarData[(comma + 1)..]);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            circle.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            initials.Visibility = Visibility.Collapsed;
        }
        catch
        {
            circle.Fill = new SolidColorBrush(Color.FromRgb(52, 57, 77));
        }
    }

    private static string GetInitials(string? nickname)
    {
        var parts = (nickname ?? "RR").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "RR";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return (parts[0][0].ToString() + parts[^1][0]).ToUpperInvariant();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _closing = true;
        _sessionCancellation.Cancel();
        _polling?.Cancel();
        if (!string.IsNullOrWhiteSpace(_token))
        {
            if (_matchActive)
            {
                try { await _api.PostAsync("/v1/match/decline", new JsonObject()); } catch { }
            }
            if (_roomWaiting)
            {
                try { await _api.PostAsync("/v1/rooms/cancel", new JsonObject()); } catch { }
            }
        }
        _polling?.Dispose();
        _gameSession.Dispose();
        _sessionCancellation.Dispose();
    }
}
