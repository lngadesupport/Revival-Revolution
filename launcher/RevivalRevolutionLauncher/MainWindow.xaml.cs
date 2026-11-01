using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public partial class MainWindow : Window
{
    private readonly string _root = Path.GetFullPath(AppContext.BaseDirectory);
    private readonly LauncherConfigService _config = new();
    private readonly PythonRuntimeService _python = new();
    private readonly GameLaunchService _gameLaunch = new();
    private readonly LauncherDiagnosticService _diagnostics = new();
    private readonly DistributionLocator _distributionLocator = new();
    private string? _distributionRoot;
    private readonly LauncherUpdateService _updates = new();
    private string? _gameFolder;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionLabel.Text = version is null ? "versão desconhecida" : $"v{version.Major}.{version.Minor}.{version.Build}";
        _gameFolder = _config.GetConfiguredGameFolder(_root);
        if (!IsValidFolder(_gameFolder)) _gameFolder = null;
        _distributionRoot = _distributionLocator.Find(_root, _gameFolder);
        GameFolderBox.Text = _gameFolder ?? "Pasta não selecionada";
        Log("Revival Revolution Launcher iniciado.");
        Log("Selecione a pasta que contém ufo.exe e ufo_Data\\Managed.");
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
        if (!IsValidFolder(folder))
        {
            MessageBox.Show(this, "A pasta selecionada não contém ufo.exe e ufo_Data\\Managed.", "Instalação inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _gameFolder = folder;
        _distributionRoot = _distributionLocator.Find(_root, folder);
        GameFolderBox.Text = folder;
        _config.SaveGameFolder(_root, folder, _distributionRoot);
        FooterStatus.Text = "Pasta do jogo salva.";
        Log($"Pasta selecionada: {folder}");
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!IsValidFolder(_gameFolder))
        {
            MessageBox.Show(this, "Escolha primeiro a pasta correta do jogo.", "Pasta não configurada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
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

        var managedPath = Path.Combine(_gameFolder!, "ufo_Data", "Managed");
        var emptyManagedDlls = Directory.EnumerateFiles(managedPath, "*.dll")
            .Where(path => new FileInfo(path).Length == 0).Select(Path.GetFileName).ToArray();
        if (emptyManagedDlls.Length > 0)
        {
            var detail = string.Join(", ", emptyManagedDlls);
            Log("Inicialização bloqueada: DLLs vazias em Managed: " + detail);
            MessageBox.Show(this,
                "A instalação do jogo contém DLLs vazias em ufo_Data\\Managed: " + detail +
                ". Restaure esses arquivos de uma instalação íntegra antes de iniciar.",
                "Instalação do jogo incompleta", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

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
            Log("Inicialização offline iniciada. Os detalhes do processo estão em Launcher\\Logs.");
            FooterStatus.Text = "Jogo em inicialização.";
        });
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
        if (_busy) return;
        await RunBusyAsync("Executando diagnóstico...", async token =>
        {
            var python = _python.FindRuntime(_root);
            _distributionRoot = _distributionLocator.Find(_root, _gameFolder);
            var items = await Task.Run(() => _diagnostics.Run(_root, _gameFolder, python), token);
            foreach (var item in items) Log($"{(item.Passed ? "OK" : "FALHA")} — {item.Name}: {item.Detail}");
            FooterStatus.Text = $"Diagnóstico: {items.Count(x => x.Passed)}/{items.Count} verificações aprovadas.";
        });
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // A checagem automática não bloqueia a abertura caso o GitHub esteja indisponível.
        try
        {
            await CheckForUpdatesAsync(automatic: true);
        }
        catch (Exception ex)
        {
            Log("Não foi possível verificar atualizações automaticamente: " + ex.Message);
        }
    }

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try { await CheckForUpdatesAsync(automatic: false); }
        catch (Exception ex) { Log("ERRO ao atualizar: " + ex.Message); FooterStatus.Text = "Falha ao verificar/aplicar atualização."; }
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

            Log($"Nova versão disponível: {update.Version}");
            if (string.IsNullOrWhiteSpace(update.PackageUrl) || update.Files.Count == 0)
                throw new InvalidDataException("Manifesto remoto incompleto: packageUrl/files ausentes.");

            var choice = MessageBox.Show(this,
                $"A versão {update.Version} está disponível.\n\n{update.ReleaseNotes}\n\nDeseja baixar e instalar agora?",
                "Atualização disponível", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice != MessageBoxResult.Yes)
            {
                Log("Atualização adiada pelo usuário.");
                return;
            }

            var script = await _updates.DownloadAndStageAsync(_root, update, CreateProgress(), token);
            Log("Atualização verificada e preparada. O launcher será fechado para aplicar os arquivos.");
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
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
    private void Home_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Página inicial.";
    private void Multiplayer_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Multiplayer experimental: utilize as bridges e scripts existentes. Compatibilidade entre máquinas ainda precisa de validação.";
}
