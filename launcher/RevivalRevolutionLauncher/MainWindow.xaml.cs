using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public partial class MainWindow : Window
{
    private readonly string _root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private readonly LauncherConfigService _config = new();
    private readonly PythonRuntimeService _python = new();
    private readonly GamePackageInstallerService _installer = new();
    private readonly GameInstallationService _gameInstallation = new();
    private readonly LauncherDiagnosticService _diagnostics = new();
    private readonly LauncherUpdateService _updates = new();
    private string? _gameFolder;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _gameFolder = _config.GetConfiguredGameFolder(_root);
        if (!IsValidFolder(_gameFolder)) _gameFolder = null;
        GameFolderBox.Text = _gameFolder ?? "Pasta não selecionada";
        Log("Revival Revolution Launcher iniciado.");
        Log("Selecione a pasta que contém ufo.exe e ufo_Data\\Managed.");
    }

    private static bool IsValidFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) &&
        File.Exists(Path.Combine(folder, "ufo.exe")) &&
        Directory.Exists(Path.Combine(folder, "ufo_Data", "Managed"));

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Selecione ufo.exe", Filter = "Executável do jogo (ufo.exe)|ufo.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var folder = Path.GetDirectoryName(dialog.FileName)!;
        if (!IsValidFolder(folder))
        {
            MessageBox.Show(this, "A pasta selecionada não contém ufo.exe e ufo_Data\\Managed.", "Instalação inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _gameFolder = folder;
        GameFolderBox.Text = folder;
        _config.SaveGameFolder(_root, folder);
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

        var script = Path.Combine(_root, "offline_launcher.py");
        if (!File.Exists(script))
        {
            Log("offline_launcher.py não encontrado na raiz da distribuição.");
            FooterStatus.Text = "Script de inicialização ausente.";
            return;
        }

        var python = _python.FindRuntime(_root);
        if (python is null && MessageBox.Show(this, "O Python portátil não está instalado. Deseja baixá-lo agora?",
            "Runtime necessário", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunBusyAsync("Preparando inicialização...", async token =>
        {
            python ??= await _python.InstallAsync(_root, CreateProgress(), token);
            _gameInstallation.StartOffline(python, script, _root);
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
            var items = _diagnostics.Run(_root, _gameFolder, python);
            foreach (var item in items) Log($"{(item.Passed ? "OK" : "FALHA")} — {item.Name}: {item.Detail}");
            FooterStatus.Text = $"Diagnóstico: {items.Count(x => x.Passed)}/{items.Count} verificações aprovadas.";
            await Task.CompletedTask;
        });
    }

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync("Verificando atualizações...", async token =>
        {
            var update = await _updates.CheckAsync(_root, token);
            if (update is null)
            {
                Log("Nenhuma versão mais recente encontrada. Se o manifesto ainda usa a URL de exemplo, configure o servidor de atualizações.");
                return;
            }
            Log($"Versão disponível: {update.Version}");
            if (string.IsNullOrWhiteSpace(update.PackageUrl) || update.Files.Count == 0)
                throw new InvalidDataException("Manifesto remoto incompleto: packageUrl/files ausentes.");
            if (MessageBox.Show(this, $"Atualização {update.Version} disponível.\\n\\n{update.ReleaseNotes}\\n\\nBaixar e preparar agora?", "Atualização", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            var script = await _updates.DownloadAndStageAsync(_root, update, CreateProgress(), token);
            Log("Atualização preparada. O launcher será fechado para aplicar os arquivos.");
            var apply = new ProcessStartInfo("cmd.exe") { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true };
            apply.ArgumentList.Add("/c");
            apply.ArgumentList.Add(script);
            Process.Start(apply);
            Dispatcher.Invoke(Close);
        });
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken,Task> operation)
    {
        if (_busy) return;
        _busy = true; FooterStatus.Text = status;
        using var cts = new CancellationTokenSource();
        try { await operation(cts.Token); if (!IsVisible) return; FooterStatus.Text = "Operação concluída."; }
        catch (OperationCanceledException) { Log("Operação cancelada."); FooterStatus.Text = "Operação cancelada."; }
        catch (Exception ex) { Log("ERRO: " + ex.Message); FooterStatus.Text = "Operação terminou com erro."; }
        finally { _busy = false; }
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
