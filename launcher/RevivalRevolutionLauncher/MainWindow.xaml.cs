using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
            MessageBox.Show(this, "Escolha primeiro a pasta correta do jogo.", "Pasta não configurada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var script = Path.Combine(_root, "offline_launcher.py");
        if (!File.Exists(script)) { Log("offline_launcher.py não encontrado na raiz da distribuição."); return; }
        var python = _python.FindRuntime(_root);
        if (python is null)
        {
            var answer = MessageBox.Show(this, "O Python portátil não está instalado. Deseja baixá-lo agora?", "Runtime necessário", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            await RunBusyAsync("Preparando Python portátil...", async token =>
            {
                python = await _python.InstallAsync(_root, CreateProgress(), token);
            });
            if (python is null) return;
        }
        try
        {
            var psi = new ProcessStartInfo(python) { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-u"); psi.ArgumentList.Add(script);
            Process.Start(psi);
            Log("Inicialização offline solicitada ao script original.");
            FooterStatus.Text = "Inicialização enviada ao offline_launcher.py.";
        }
        catch (Exception ex) { Log($"Falha ao iniciar: {ex.Message}"); }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var answer = MessageBox.Show(this,
            "O processo baixará os dois pacotes configurados e extrairá os arquivos na pasta do launcher. Faça backup da instalação antes de continuar. Deseja prosseguir?",
            "Instalar / reparar distribuição", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        await RunBusyAsync("Baixando e preparando distribuição...", async token =>
        {
            await _installer.InstallAsync(_root, CreateProgress(), token);
            Log("Pacotes baixados e extraídos. Iniciando verificação SHA-256...");
            var results = await _installer.VerifyAsync(_root, token);
            var invalid = results.Where(x => !x.IsValid).ToArray();
            if (invalid.Length > 0)
            {
                Log($"Integridade: {invalid.Length} arquivo(s) ausente(s) ou alterado(s).");
                await _installer.RepairAsync(_root, invalid, token);
                Log("Reparo seletivo concluído e verificado.");
            }
            else Log($"Integridade aprovada: {results.Count} arquivo(s).");
            var runtime = _python.FindRuntime(_root) ?? await _python.InstallAsync(_root, CreateProgress(), token);
            Log($"Python portátil pronto: {runtime}");
        });
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
            if (update is null) { Log("Manifesto launcher-update.json não configurado."); return; }
            Log($"Versão disponível: {update.Version}");
            if (string.IsNullOrWhiteSpace(update.PackageUrl) || update.Files.Count == 0)
                throw new InvalidDataException("Manifesto remoto incompleto: packageUrl/files ausentes.");
            if (MessageBox.Show(this, $"Atualização {update.Version} disponível.\n\n{update.ReleaseNotes}\n\nBaixar e preparar agora?", "Atualização", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            var script = await _updates.DownloadAndStageAsync(_root, update, CreateProgress(), token);
            Log("Atualização preparada. O launcher será fechado para aplicar os arquivos.");
            Process.Start(new ProcessStartInfo("cmd.exe") { Arguments = $"/c \"{script}\"", WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true });
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
