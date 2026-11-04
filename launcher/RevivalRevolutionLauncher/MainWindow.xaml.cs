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
    private readonly GameCompatibilityService _gameCompatibility = new();
    private readonly LauncherDiagnosticService _diagnostics = new();
    private readonly DistributionLocator _distributionLocator = new();
    private string? _distributionRoot;
    private readonly LauncherUpdateService _updates = new();
    private readonly LauncherSettingsService _settingsService = new();
    private LauncherSettings _settings = new();
    private string? _gameFolder;
    private bool _busy;
    private readonly DispatcherTimer _gameStateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _radminHostTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _radminHostStarting;
    private bool _radminHostFirewallReady;
    private bool _radminHostTaskReady;
    private bool _radminHostDisabled;
    private bool _gameRunning;
    private bool _launchPending;
    private DateTime _launchRequestedAt;
    private const int LaunchDetectionTimeoutSeconds = 45;
    private string _lastGameStateFooterStatus = string.Empty;
    private string _lastDashboardState = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
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
        _radminHostTimer.Tick += RadminHostTimer_Tick;
        _radminHostTimer.Start();
        RefreshGameState();
        Log("Revival Revolution Launcher iniciado.");
        Log("Selecione a pasta que contém ufo.exe e ufo_Data\\Managed.");
    }

    private async void RadminHostTimer_Tick(object? sender, EventArgs e)
    {
        if (_radminHostStarting || _radminHostDisabled || _radminHostTaskReady) return;
        var endpoint = RankedApiClient.LoadEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = RadminHostService.DefaultEndpoint;
        var hostAddress = RadminHostService.GetLocalRadminAddress(endpoint);
        if (hostAddress is null) return;

        _radminHostStarting = true;
        try
        {
            var python = _python.FindRuntime(_root);
            if (python is null)
            {
                FooterStatus.Text = "Preparando o servidor privado Radmin pela primeira vez...";
                Log("Este computador é o anfitrião Radmin. Instalando o runtime necessário para iniciar a API automaticamente.");
                python = await _python.InstallAsync(_root, CreateProgress(), CancellationToken.None);
            }

            if (!_radminHostFirewallReady)
            {
                FooterStatus.Text = "Configurando o firewall privado Radmin (autorização única)...";
                await _radminHost.EnsureFirewallAsync(python, hostAddress, CancellationToken.None);
                _radminHostFirewallReady = true;
            }

            await _radminHost.EnsureHostServerAsync(_root, python, hostAddress, CancellationToken.None);
            _radminHostTaskReady = true;
            _radminHostTimer.Stop();
            FooterStatus.Text = $"Servidor privado ativo em http://{hostAddress}:8765";
            Log($"API Ranked iniciada/verificada automaticamente em http://{hostAddress}:8765.");
        }
        catch (Exception ex)
        {
            _radminHostDisabled = true;
            FooterStatus.Text = "Não foi possível iniciar o servidor Radmin automaticamente.";
            Log("ERRO ao iniciar o servidor Radmin em segundo plano: " + ex.Message);
        }
        finally
        {
            _radminHostStarting = false;
        }
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
            HeroBackground.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(background));
        else
            Log("Arte de fundo opcional ausente: Assets\\metal-revolution-background.jpg");

        if (File.Exists(logo))
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(logo));
            MetalLogo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(logo));
            MetalLogo.Visibility = Visibility.Visible;
            LogoFallback.Visibility = Visibility.Collapsed;
        }
        else
            Log("Logo PNG opcional ausente: Assets\\metal-revolution-logo.png");
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
            _gameLaunch.StartOffline(python, script, _distributionRoot!, _settings);
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
        var compatibility = _gameCompatibility.Inspect(_gameFolder);
        DashboardBuildStatus.Text = compatibility.IsDetected
            ? $"Jogo detectado • {compatibility.VersionLabel} • {compatibility.Detail}"
            : compatibility.Detail;
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
            ("private", PrivateMatchNavButton), ("network", RadminNavButton),
            ("diagnostics", DiagnosticsNavButton), ("updates", UpdatesNavButton),
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
            _settingsService.Save(window.Result);
            _settings = window.Result;
            Log("Configurações salvas.");
            FooterStatus.Text = "Configurações atualizadas.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível salvar as configurações: " + ex.Message,
                "Erro ao salvar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "A instalação e o reparo automáticos estão temporariamente bloqueados por segurança. " +
            "Os pacotes ZIP ainda não possuem hashes SHA-256 autenticados por um canal independente. " +
            "Um manifesto de checksums dentro do próprio pacote não comprova sua origem. " +
            "Nenhum arquivo será baixado, extraído ou sobrescrito. A inicialização de uma instalação existente continua disponível.",
            "Instalação protegida", MessageBoxButton.OK, MessageBoxImage.Information);
        Log("Instalação/reparo automático bloqueados até autenticação independente dos pacotes.");
        FooterStatus.Text = "Instalação automática aguardando hashes confiáveis.";
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("diagnostics");
        if (_busy) return;
        await RunBusyAsync("Executando diagnóstico...", async token =>
        {
            var python = _python.FindRuntime(_root);
            _distributionRoot = _distributionLocator.Find(_root, _gameFolder);
            var items = await Task.Run(() => _diagnostics.Run(_root, _gameFolder, python), token);
            foreach (var item in items)
                Log($"{(item.Optional ? "INFO" : item.Passed ? "OK" : "FALHA")} — {item.Name}: {item.Detail}");
            var required = items.Where(item => !item.Optional).ToArray();
            FooterStatus.Text = $"Diagnóstico: {required.Count(item => item.Passed)}/{required.Length} verificações essenciais aprovadas.";
        });
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // A checagem automática não bloqueia a abertura caso o GitHub esteja indisponível.
        try
        {
            if (_settings.CheckUpdatesOnStartup)
                await CheckForUpdatesAsync(automatic: true);
        }
        catch (Exception ex)
        {
            Log("Não foi possível verificar atualizações automaticamente: " + ex.Message);
        }
    }

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("updates");
        if (_busy) return;
        try { await CheckForUpdatesAsync(automatic: false); }
        catch (Exception ex) { Log("ERRO ao atualizar: " + ex.Message); FooterStatus.Text = "Falha ao verificar/aplicar atualização."; }
    }

    private void ShowUpdateProgress(string version, bool repair)
    {
        UpdateVersionText.Text = repair
            ? $"Reparo seletivo dos arquivos do launcher — {version}"
            : $"Versão {version} pronta para instalação";
        UpdateStageText.Text = "Conectando ao servidor oficial...";
        UpdateProgressDetail.Text = "Aguardando início do download...";
        UpdatePercentText.Text = "—";
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.IsIndeterminate = true;
        UpdateOverlay.Visibility = Visibility.Visible;
        UpdateOverlay.Opacity = 0;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        UpdateOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private async Task CheckForUpdatesAsync(bool automatic)
    {
        await RunBusyAsync("Verificando atualizações...", async token =>
        {
            var update = await _updates.CheckAsync(_root, token);
            if (update is null)
            {
                if (!automatic) Log("O launcher já está atualizado.");
                return;
            }

            Log(update.IsRepair
                ? $"Arquivos do launcher ausentes ou alterados; reparo seletivo da versão {update.Version} disponível."
                : $"Nova versão disponível: {update.Version}");
            if (string.IsNullOrWhiteSpace(update.PackageUrl) || update.Files.Count == 0)
                throw new InvalidDataException("Manifesto remoto incompleto: packageUrl/files ausentes.");

            var prompt = update.IsRepair
                ? $"Foram encontrados {update.Files.Count} arquivo(s) do launcher ausentes ou com SHA-256 diferente.\n\nA versão instalada é {update.Version}. Somente os arquivos divergentes serão substituídos.\n\nDeseja reparar agora?"
                : $"A versão {update.Version} está disponível.\n\n{update.ReleaseNotes}\n\nDeseja baixar e instalar agora?";
            var choice = MessageBox.Show(this, prompt,
                update.IsRepair ? "Reparo do launcher" : "Atualização disponível",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice != MessageBoxResult.Yes)
            {
                Log("Atualização adiada pelo usuário.");
                return;
            }

            ShowUpdateProgress(update.Version, update.IsRepair);
            string script;
            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    var received = p.BytesReceived / 1048576d;
                    var total = p.TotalBytes is long bytes ? $" / {bytes / 1048576d:F1} MB" : "";
                    var resumed = p.IsResuming ? " • retomada" : "";
                    UpdateProgressDetail.Text = $"{p.FileName}: {received:F1} MB{total}{resumed}";
                    if (p.Percent is double percent)
                    {
                        UpdateProgressBar.IsIndeterminate = false;
                        UpdateProgressBar.Value = Math.Clamp(percent, 0, 100);
                        UpdatePercentText.Text = $"{percent:F0}%";
                    }
                    else
                    {
                        UpdateProgressBar.IsIndeterminate = true;
                        UpdatePercentText.Text = "—";
                    }
                });
                var stageProgress = new Progress<UpdateOperationProgress>(stage =>
                {
                    UpdateStageText.Text = stage.Stage switch
                    {
                        "verify" => "Verificando integridade do pacote...",
                        "extract" => "Preparando arquivos da atualização...",
                        "finalize" => "Finalizando preparação...",
                        _ => "Baixando pacote de atualização..."
                    };
                    UpdateProgressDetail.Text = stage.Detail;
                    if (stage.Stage != "download")
                    {
                        UpdateProgressBar.IsIndeterminate = true;
                        UpdatePercentText.Text = stage.Stage == "verify" ? "SHA-256" : "…";
                    }
                });

                UpdateStageText.Text = "Baixando pacote de atualização...";
                script = await _updates.DownloadAndStageAsync(_root, update, progress, token, stageProgress);
                UpdateProgressBar.IsIndeterminate = false;
                UpdateProgressBar.Value = 100;
                UpdatePercentText.Text = "100%";
                UpdateStageText.Text = "Download verificado e arquivos preparados.";
                UpdateProgressDetail.Text = "SHA-256 validado • Preparação concluída";
            }
            catch
            {
                UpdateOverlay.Visibility = Visibility.Collapsed;
                UpdateOverlay.Opacity = 0;
                throw;
            }

            Log(update.IsRepair
                ? $"Reparo seletivo de {update.Files.Count} arquivo(s) verificado e preparado."
                : "Atualização verificada e preparada. O launcher será fechado para aplicar os arquivos.");
            var apply = new ProcessStartInfo("cmd.exe") { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true };
            apply.ArgumentList.Add("/c");
            apply.ArgumentList.Add(script);
            Process.Start(apply);
            Close();
        });
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
        _radminHostTimer.Stop();
        base.OnClosed(e);
    }

    private void Home_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Página inicial.";

    private async void PrivateMatch_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("private");
        FooterStatus.Text = "PRIVATE MATCH — abrindo salas por código.";
        Log("Central Private Match aberta.");
        var configuredEndpoint = RankedApiClient.LoadEndpoint();
        if (string.IsNullOrWhiteSpace(configuredEndpoint)) configuredEndpoint = RadminHostService.DefaultEndpoint;
        var isConfiguredHost = RadminHostService.GetLocalRadminAddress(configuredEndpoint) is not null;
        if (isConfiguredHost)
        {
            while (!_radminHostTaskReady && !_radminHostDisabled) await Task.Delay(200);
            if (_radminHostDisabled)
            {
                MessageBox.Show(this, "O servidor privado não foi inicializado. Confira o log do launcher e autorize a configuração única do firewall do Windows.",
                    "Servidor Radmin indisponível", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
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
            var endpoint = RankedApiClient.LoadEndpoint();
            var hostAddress = string.IsNullOrWhiteSpace(endpoint) ? null : RadminHostService.GetLocalRadminAddress(endpoint);
            await _radminHost.EnsureFirewallAsync(python, hostAddress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível preparar a comunicação P2P pela Radmin: " + ex.Message,
                "Firewall Radmin", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _config.SaveGameFolder(_root, _gameFolder, distributionRoot);
        var window = new OnlineMatchWindow(distributionRoot, _gameFolder, python) { Owner = this };
        window.ShowDialog();
        RefreshGameState();
    }

    private void RadminNetwork_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation("network");
        var connected = RadminHostService.HasRadminAddress();
        var endpoint = RankedApiClient.LoadEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = RadminHostService.DefaultEndpoint;
        var localHost = RadminHostService.GetLocalRadminAddress(endpoint);
        var message = connected
            ? localHost is null
                ? "Radmin VPN conectada. Este PC participa como jogador; o servidor da sala fica no anfitrião."
                : $"Radmin VPN conectada. Este PC pode hospedar salas em {localHost}:8765."
            : "Radmin VPN desconectada. Conecte-se à mesma rede Radmin dos outros jogadores.";
        FooterStatus.Text = message;
        Log(message);
        MessageBox.Show(this, message, "Rede Radmin", MessageBoxButton.OK,
            connected ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
