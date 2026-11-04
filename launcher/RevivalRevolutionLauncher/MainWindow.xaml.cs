using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public partial class MainWindow : Window
{
    private readonly string _root = Path.GetFullPath(AppContext.BaseDirectory);
    private readonly LauncherConfigService _config = new();
    private readonly PythonRuntimeService _python = new();
    private readonly RadminHostService _radminHost = new();
    private readonly GameLaunchService _gameLaunch = new();
    private readonly LauncherDiagnosticService _diagnostics = new();
    private readonly DistributionLocator _distributionLocator = new();
    private string? _distributionRoot;
    private readonly LauncherSettingsService _settingsService = new();
    private LauncherSettings _settings = new();
    private string? _gameFolder;
    private bool _busy;
    private readonly DispatcherTimer _gameStateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _gameRunning;
    private bool _launchPending;
    private DateTime _launchRequestedAt;
    private const int LaunchDetectionTimeoutSeconds = 45;
    private string _lastGameStateFooterStatus = string.Empty;
    private string _lastDashboardState = string.Empty;
    private OnlineMatchWindow? _privateMatchView;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        _settings = _settingsService.Load();
        SetActiveNavigation("offline");
        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionLabel.Text = version is null ? "versão desconhecida" : $"v{version.Major}.{version.Minor}.{version.Build}";
        _gameFolder = _config.GetConfiguredGameFolder(_root);
        if (!IsValidFolder(_gameFolder)) _gameFolder = null;
        _distributionRoot = _distributionLocator.Find(_root, _gameFolder);
        GameFolderBox.Text = _gameFolder ?? "CAMINHO DA PASTA";
        LoadVisualAssets();
        _gameStateTimer.Tick += (_, _) => RefreshGameState();
        _gameStateTimer.Start();
        RefreshGameState();
        Log("Revival Revolution Launcher iniciado.");
        Log("Selecione a pasta que contém ufo.exe e ufo_Data\\Managed.");
    }

    private void MainContent_Loaded(object sender, RoutedEventArgs e)
    {
        // Staggered entrance: soft fade plus a short upward settle, with no impact on launch logic.
        AnimateEntrance(MainContent, 0, 420, 10);
        AnimateEntrance(WindowChrome, 45, 360, 4);
        AnimateEntrance(HeaderPanel, 90, 460, 8);
        AnimateEntrance(SidebarPanel, 130, 520, 12);
        AnimateEntrance(HeroPanel, 190, 560, 14);
        AnimateEntrance(QuickAccessPanel, 270, 520, 12);
        AnimateEntrance(BottomPanel, 340, 520, 10);
    }

    private static void AnimateEntrance(UIElement element, int delayMs, int durationMs, double offsetY)
    {
        if (element is null) return;
        element.Opacity = 0;
        var transform = element.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }
        transform.Y = offsetY;

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        var slide = new System.Windows.Media.Animation.DoubleAnimation(offsetY, 0,
            TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        transform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void LoadVisualAssets()
    {
        var assets = Path.Combine(_root, "Assets");
        var background = Path.Combine(assets, "metal-revolution-background.jpg");
        var logo = Path.Combine(assets, "metal-revolution-logo.png");

        if (File.Exists(background))
            HeroBackground.Source = LoadBitmap(background, 1600);
        else
            Log("Arte de fundo opcional ausente: Assets\\metal-revolution-background.jpg");

        if (File.Exists(logo))
        {
            MetalLogo.Source = LoadBitmap(logo, 640);
            MetalLogo.Visibility = Visibility.Visible;
            LogoFallback.Visibility = Visibility.Collapsed;
        }
        else
            Log("Logo PNG opcional ausente: Assets\\metal-revolution-logo.png");
    }

    private static System.Windows.Media.Imaging.BitmapImage LoadBitmap(string path, int decodeWidth)
    {
        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private bool IsValidFolder(string? folder) => _gameLaunch.IsValidGameFolder(folder);

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta Metal Revolution",
            Multiselect = false,
            FolderName = _gameFolder is not null && Directory.Exists(_gameFolder)
                ? _gameFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (dialog.ShowDialog(this) != true) return;
        var folder = dialog.FolderName;
        _gameFolder = folder;
        _distributionRoot = _distributionLocator.Find(_root, folder);
        GameFolderBox.Text = folder;
        _config.SaveGameFolder(_root, folder, _distributionRoot);
        RefreshGameState();
        FooterStatus.Text = IsValidFolder(folder)
            ? "Pasta do jogo salva e validada."
            : "Pasta salva, mas a instalação não passou na validação.";
        Log(IsValidFolder(folder)
            ? $"Pasta selecionada e validada: {folder}"
            : $"Pasta selecionada, mas inválida: {folder}");
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("offline");
        if (_busy || IsGameRunning()) return;
        RefreshGameState();
        if (!IsValidFolder(_gameFolder))
        {
            MessageBox.Show(this, "Escolha primeiro a pasta correta do jogo.", "Pasta não configurada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_settings.ConfirmBeforeLaunch)
        {
            var choice = MessageBox.Show(this, "Iniciar Metal Revolution no modo offline agora?",
                "Confirmar inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) return;
        }

        _distributionRoot = _distributionLocator.Find(_root, _gameFolder);
        if (_distributionRoot is null)
        {
            Log("Distribuição incompleta: não encontrei offline_launcher.py junto ao launcher nem na pasta acima do jogo.");
            FooterStatus.Text = "Arquivos de inicialização ausentes.";
            MessageBox.Show(this,
                "Não encontrei os scripts da distribuição (offline_launcher.py, inject_offline.py e inject_overlay.py). " +
                "Mantenha esses arquivos na pasta do launcher ou na pasta que contém a pasta do jogo.",
                "Distribuição incompleta", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var script = Path.Combine(_distributionRoot, "offline_launcher.py");
        var requiredScripts = new[] { "inject_offline.py", "inject_overlay.py" };
        var missingScripts = requiredScripts.Where(name => !File.Exists(Path.Combine(_distributionRoot, name))).ToArray();
        if (missingScripts.Length > 0)
        {
            Log("Scripts essenciais ausentes: " + string.Join(", ", missingScripts));
            MessageBox.Show(this, "A distribuição está incompleta. Faltam: " + string.Join(", ", missingScripts),
                "Arquivos ausentes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var offlineBridge = new[] {
            Path.Combine(_distributionRoot, "MetalOfflineBridgeImGuiV3.dll"),
            Path.Combine(_gameFolder!, "MetalOfflineBridgeImGuiV3.dll")
        }.FirstOrDefault(File.Exists);
        if (offlineBridge is null)
        {
            Log("MetalOfflineBridgeImGuiV3.dll não encontrado na distribuição nem na pasta do jogo.");
            MessageBox.Show(this, "A bridge offline (MetalOfflineBridgeImGuiV3.dll) não foi encontrada. Não é seguro iniciar.",
                "Dependência ausente", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Preserve compatibility with the creator's known-working package. Some builds include
        // zero-length managed placeholders; report them, but do not block the original bootstrap.
        var managedPath = Path.Combine(_gameFolder!, "ufo_Data", "Managed");
        var emptyManagedDlls = Directory.EnumerateFiles(managedPath, "*.dll")
            .Where(path => new FileInfo(path).Length == 0).Select(Path.GetFileName).ToArray();
        if (emptyManagedDlls.Length > 0)
            Log("Aviso: DLLs vazias em Managed (mantendo o comportamento do launcher original): " +
                string.Join(", ", emptyManagedDlls));

        var python = _python.FindRuntime(_root);
        if (python is null && MessageBox.Show(this, "O Python portátil não está instalado. Deseja baixá-lo agora?",
            "Runtime necessário", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunBusyAsync("Preparando inicialização...", async token =>
        {
            python ??= await _python.InstallAsync(_root, CreateProgress(), token);
            // The legacy bootstrap resolves its runtime marker from its own working directory/game folder.
            File.WriteAllText(Path.Combine(_distributionRoot!, "python-runtime.txt"), python);
            File.WriteAllText(Path.Combine(_gameFolder!, "python-runtime.txt"), python);
            _config.SaveGameFolder(_root, _gameFolder!, _distributionRoot);
            _gameLaunch.StartOffline(python, script, _distributionRoot!);
            _launchPending = true;
            _launchRequestedAt = DateTime.UtcNow;
            Log("Inicialização offline iniciada. Aguardando o processo ufo.exe; o PLAY ficará bloqueado por até 45 segundos.");
            RefreshGameState();
        });
    }

    private bool IsGameRunning()
    {
        if (string.IsNullOrWhiteSpace(_gameFolder)) return false;
        var expectedExe = Path.GetFullPath(Path.Combine(_gameFolder, "ufo.exe"));
        foreach (var process in Process.GetProcessesByName("ufo"))
        {
            try
            {
                var actualExe = process.MainModule?.FileName;
                // If Windows exposes the executable path, only this selected installation counts.
                if (!string.IsNullOrWhiteSpace(actualExe))
                {
                    if (string.Equals(Path.GetFullPath(actualExe), expectedExe, StringComparison.OrdinalIgnoreCase))
                        return true;
                    continue;
                }

                // Fail safe: if the process exists but Windows hides its path, keep PLAY locked.
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied is not proof that the game is closed.
                return true;
            }
            catch (InvalidOperationException)
            {
                // The process exited while being inspected; continue with the remaining processes.
            }
            catch (NotSupportedException)
            {
                return true;
            }
            finally { process.Dispose(); }
        }
        return false;
    }

    private void RefreshGameState()
    {
        var valid = IsValidFolder(_gameFolder);
        _gameRunning = valid && IsGameRunning();

        if (_gameRunning)
            _launchPending = false;
        else if (_launchPending && DateTime.UtcNow - _launchRequestedAt >= TimeSpan.FromSeconds(LaunchDetectionTimeoutSeconds))
        {
            _launchPending = false;
            Log("O processo ufo.exe não foi detectado após 45 segundos. PLAY liberado; confira o log de inicialização se o jogo não abriu.");
        }

        if (_gameRunning)
        {
            GameStateLabel.Text = "JOGO ABERTO";
            GameStateBorder.Background = new SolidColorBrush(Color.FromRgb(35, 130, 75));
            PlayButton.IsEnabled = false;
            SetGameStateFooter("O jogo está em execução. PLAY bloqueado.");
        }
        else if (_launchPending)
        {
            GameStateLabel.Text = "INICIANDO...";
            GameStateBorder.Background = new SolidColorBrush(Color.FromRgb(190, 125, 35));
            PlayButton.IsEnabled = false;
            SetGameStateFooter("Aguardando abertura do jogo...");
        }
        else if (valid)
        {
            GameStateLabel.Text = "PREPARADO";
            GameStateBorder.Background = new SolidColorBrush(Color.FromRgb(35, 145, 80));
            PlayButton.IsEnabled = !_busy;
            SetGameStateFooter("Jogo preparado. Pronto para iniciar no modo offline.");
        }
        else if (string.IsNullOrWhiteSpace(_gameFolder))
        {
            GameStateLabel.Text = "NENHUM";
            GameStateBorder.Background = new SolidColorBrush(Color.FromRgb(85, 91, 102));
            PlayButton.IsEnabled = false;
            SetGameStateFooter("Selecione a pasta do jogo para começar.");
        }
        else
        {
            GameStateLabel.Text = "INVÁLIDO";
            GameStateBorder.Background = new SolidColorBrush(Color.FromRgb(170, 45, 55));
            PlayButton.IsEnabled = false;
            SetGameStateFooter("A pasta selecionada não passou na validação.");
        }

        var dashboardState = _gameRunning ? "EM EXECUÇÃO"
            : _launchPending ? "INICIANDO"
            : valid ? "VALIDADA"
            : string.IsNullOrWhiteSpace(_gameFolder) ? "NÃO CONFIGURADA" : "INVÁLIDA";
        DashboardLocalStatus.Text = dashboardState;
        DashboardLocalStatus.Foreground = new SolidColorBrush(_gameRunning || valid
            ? Color.FromRgb(120, 225, 155)
            : string.IsNullOrWhiteSpace(_gameFolder)
                ? Color.FromRgb(210, 205, 220)
                : Color.FromRgb(255, 125, 135));
        if (!string.Equals(_lastDashboardState, dashboardState, StringComparison.Ordinal))
        {
            _lastDashboardState = dashboardState;
            FadeStateChange(GameStateBorder);
            FadeStateChange(DashboardLocalStatus);
        }
    }

    private static void FadeStateChange(UIElement element)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(0.55, 1,
            TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void SetGameStateFooter(string message)
    {
        // The one-second process watcher must not erase operation results or progress messages.
        if (string.Equals(_lastGameStateFooterStatus, message, StringComparison.Ordinal)) return;
        _lastGameStateFooterStatus = message;
        FooterStatus.Text = message;
    }

    private void SetActiveNavigation(string active)
    {
        var buttons = new (string Key, System.Windows.Controls.Button Button)[]
        {
            ("offline", OfflineNavButton),
            ("private", PrivateMatchNavButton), ("mods", ModListNavButton),
            ("diagnostics", DiagnosticsNavButton),
            ("settings", SettingsNavButton)
        };

        foreach (var item in buttons)
        {
            var selected = item.Key == active;
            item.Button.Background = new SolidColorBrush(Color.FromArgb(
                selected ? (byte)205 : (byte)118,
                selected ? (byte)157 : (byte)16,
                selected ? (byte)31 : (byte)24,
                selected ? (byte)44 : (byte)42));
            item.Button.BorderBrush = new SolidColorBrush(selected
                ? Color.FromRgb(255, 82, 96)
                : Color.FromArgb(102, 255, 255, 255));
            item.Button.BorderThickness = selected ? new Thickness(1.5) : new Thickness(1);
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape &&
            PrivateMatchHost.Visibility == Visibility.Visible &&
            _privateMatchView is not null)
        {
            PrivateMatchBackRequested(_privateMatchView, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
            return;

        switch (e.Key)
        {
            case System.Windows.Input.Key.D1:
            case System.Windows.Input.Key.NumPad1:
                Launch_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case System.Windows.Input.Key.D2:
            case System.Windows.Input.Key.NumPad2:
                PrivateMatch_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case System.Windows.Input.Key.D3:
            case System.Windows.Input.Key.NumPad3:
                Diagnostics_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case System.Windows.Input.Key.D4:
            case System.Windows.Input.Key.NumPad4:
                Settings_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("settings");
        var window = new LauncherSettingsWindow(_settings, _gameFolder ?? "",
            _settingsService.GetSettingsPath(), _settingsService.GetLogDirectory())
        {
            Owner = this
        };
        if (window.ShowDialog() != true) return;

        try
        {
            var hadCustomRadminIp = _settings.UseCustomRadminIp;
            _settingsService.Save(window.Result);
            _settings = window.Result;
            if (_settings.UseCustomRadminIp)
                RankedApiClient.SaveEndpoint($"http://{_settings.CustomRadminIp}:8765");
            else if (hadCustomRadminIp)
                RankedApiClient.SaveEndpoint(RadminHostService.DefaultEndpoint);
            Log("Configurações salvas.");
            FooterStatus.Text = "Configurações atualizadas.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível salvar as configurações: " + ex.Message,
                "Erro ao salvar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("diagnostics");
        if (_busy) return;

        IReadOnlyList<DiagnosticItem>? items = null;
        _busy = true;
        IsEnabled = false;
        FooterStatus.Text = "Executando diagnóstico da instalação...";
        try
        {
            var python = _python.FindRuntime(_root);
            _distributionRoot = _distributionLocator.Find(_root, _gameFolder);
            items = await Task.Run(() => _diagnostics.Run(_root, _gameFolder, python));
            foreach (var item in items)
                Log($"{(item.Optional ? "INFO" : item.Passed ? "OK" : "FALHA")} — {item.Name}: {item.Detail}");

            var required = items.Where(item => !item.Optional).ToArray();
            FooterStatus.Text = $"Diagnóstico: {required.Count(item => item.Passed)}/{required.Length} verificações essenciais aprovadas.";
        }
        catch (Exception ex)
        {
            Log("Falha ao executar diagnóstico: " + ex.Message);
            FooterStatus.Text = "Não foi possível concluir o diagnóstico.";
            MessageBox.Show(this, "O diagnóstico não pôde ser concluído: " + ex.Message,
                "Diagnósticos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            if (IsVisible) IsEnabled = true;
            RefreshGameState();
        }

        if (items is null || !IsVisible) return;
        var window = new LauncherDiagnosticsWindow(items, _settingsService.GetLogDirectory())
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken,Task> operation)
    {
        if (_busy) return;
        _busy = true;
        IsEnabled = false;
        FooterStatus.Text = status;
        using var cts = new CancellationTokenSource();
        try { await operation(cts.Token); if (!IsVisible) return; FooterStatus.Text = "Operação concluída."; }
        catch (OperationCanceledException) { Log("Operação cancelada."); FooterStatus.Text = "Operação cancelada."; }
        catch (Exception ex) { Log("ERRO: " + ex.Message); FooterStatus.Text = "Operação terminou com erro."; }
        finally
        {
            _busy = false;
            if (IsVisible) IsEnabled = true;
            RefreshGameState();
        }
    }

    private IProgress<DownloadProgress> CreateProgress() => new Progress<DownloadProgress>(p =>
    {
        var pct = p.Percent is double value ? $" {value:F1}%" : "";
        FooterStatus.Text = $"{p.FileName}: {p.BytesReceived / 1048576d:F1} MB{pct}";
    });

    private void Log(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(message)); return; }
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        LogBox.AppendText(line);
        LogBox.ScrollToEnd();
        if (_settings.PersistLogs)
        {
            try
            {
                var directory = _settingsService.GetLogDirectory();
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "launcher.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                {
                    var previous = Path.Combine(directory, "launcher.previous.log");
                    File.Delete(previous);
                    File.Move(path, previous);
                }
                File.AppendAllText(path, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never interrupt launching or updating.
            }
        }
    }
    protected override void OnClosed(EventArgs e)
    {
        _gameStateTimer.Stop();
        if (_privateMatchView is not null)
        {
            _ = _privateMatchView.ShutdownAsync();
            _privateMatchView = null;
        }
        base.OnClosed(e);
    }

    private void Home_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Página inicial.";

    private async void PrivateMatch_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("private");
        if (_privateMatchView is not null)
        {
            DashboardPage.Visibility = Visibility.Collapsed;
            PrivateMatchHost.Visibility = Visibility.Visible;
            FooterStatus.Text = "SALA POR CÓDIGO — pronta para criar ou entrar em um convite.";
            return;
        }
        FooterStatus.Text = "SALA POR CÓDIGO — preparando conexão.";
        Log("Central Private Match aberta.");
        var configuredEndpoint = RankedApiClient.LoadEndpoint();
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
            configuredEndpoint = RadminHostService.DefaultEndpoint;
        var localHostAddress = RadminHostService.GetLocalRadminAddress(configuredEndpoint);
        var distributionRoot = _distributionRoot ?? _distributionLocator.Find(_root, _gameFolder);
        var python = _python.FindRuntime(_root);
        if (string.IsNullOrWhiteSpace(_gameFolder) || distributionRoot is null)
        {
            MessageBox.Show(this, "Configure a pasta do jogo e mantenha os scripts/DLLs da distribuição junto ao launcher antes de abrir o PRIVATE MATCH.",
                "Distribuição incompleta", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (python is null)
        {
            try
            {
                FooterStatus.Text = "Preparando runtime Python para o PRIVATE MATCH...";
                python = await _python.InstallAsync(_root, CreateProgress(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível preparar o runtime Python: " + ex.Message,
                    "Runtime necessário", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        if (!RadminHostService.HasRadminAddress())
        {
            MessageBox.Show(this, "Ligue o Radmin VPN e confirme que este computador está conectado à rede privada antes de abrir o PRIVATE MATCH.",
                "Radmin VPN desconectada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            await _radminHost.EnsureFirewallAsync(python, localHostAddress, CancellationToken.None);
            if (localHostAddress is not null)
            {
                FooterStatus.Text = "Iniciando servidor privado Radmin...";
                await _radminHost.EnsureHostServerAsync(_root, python, localHostAddress, CancellationToken.None);
                FooterStatus.Text = $"Servidor privado ativo em http://{localHostAddress}:8765";
                Log($"Servidor Radmin pronto em http://{localHostAddress}:8765.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível preparar a comunicação P2P pela Radmin: " + ex.Message,
                "Firewall Radmin", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _config.SaveGameFolder(_root, _gameFolder, distributionRoot);
        var roomView = new OnlineMatchWindow(distributionRoot, _gameFolder, python);
        roomView.BackRequested += PrivateMatchBackRequested;
        _privateMatchView = roomView;
        PrivateMatchHost.Content = roomView;
        DashboardPage.Visibility = Visibility.Collapsed;
        PrivateMatchHost.Visibility = Visibility.Visible;
        FooterStatus.Text = "SALA POR CÓDIGO — conectando ao servidor privado.";
    }

    private async void PrivateMatchBackRequested(object? sender, EventArgs e)
    {
        var view = _privateMatchView;
        _privateMatchView = null;
        PrivateMatchHost.Content = null;
        PrivateMatchHost.Visibility = Visibility.Collapsed;
        DashboardPage.Visibility = Visibility.Visible;
        SetActiveNavigation("offline");
        if (view is not null)
            await view.ShutdownAsync();
        RefreshGameState();
    }

    private void ModList_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("mods");
        FooterStatus.Text = "MOD LIST — catálogo ainda não integrado.";
        MessageBox.Show(this,
            "A lista de mods será integrada em uma etapa própria. Por enquanto, o launcher não altera nem instala mods.",
            "Mod List", MessageBoxButton.OK, MessageBoxImage.Information);
        Log("Mod List selecionada; catálogo ainda não integrado.");
    }


    private void WindowChrome_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (WindowState == WindowState.Maximized) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* Ignore a drag that ended before it began. */ }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        => ToggleWindowState();

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateWindowChrome();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
        => UpdateWindowChrome();

    private void UpdateWindowChrome()
    {
        if (MaximizeButton is null) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = char.ConvertFromUtf32(maximized ? 0xE923 : 0xE922);
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Discord_Click(object sender, RoutedEventArgs e)
    {
        const string discordUrl = "https://discord.gg/Zw2GCscKU";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = discordUrl, UseShellExecute = true });
            Log("Convite do Discord aberto no navegador padrão.");
        }
        catch (Exception ex)
        {
            Log("Não foi possível abrir o Discord: " + ex.Message);
            MessageBox.Show(this, "Não foi possível abrir o navegador automaticamente. Acesse:\n" + discordUrl,
                "Discord", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
