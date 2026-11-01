using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace RevivalRevolution;

public partial class MainWindow : Window
{
    private readonly string _root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private string? _gameFolder;

    public MainWindow()
    {
        InitializeComponent();
        _gameFolder = ReadConfiguredFolder();
        GameFolderBox.Text = _gameFolder ?? "Pasta não selecionada";
        Log("Revival Revolution Launcher iniciado.");
        Log("Selecione a pasta do jogo que contém ufo.exe e ufo_Data\\Managed.");
    }

    private string? ReadConfiguredFolder()
    {
        foreach (var path in new[] { Path.Combine(_root, "Launcher", "Config", "launcher-config.json"), Path.Combine(_root, "launcher-config.json") })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("game_folder", out var value))
                {
                    var folder = Environment.ExpandEnvironmentVariables(value.GetString() ?? "");
                    if (IsValidFolder(folder)) return folder;
                }
            }
            catch { }
        }
        var bundled = Path.Combine(_root, "Metal Revolution");
        return IsValidFolder(bundled) ? bundled : null;
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
        var configDir = Path.Combine(_root, "Launcher", "Config");
        Directory.CreateDirectory(configDir);
        var json = JsonSerializer.Serialize(new { game_folder = folder }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(configDir, "launcher-config.json"), json);
        File.WriteAllText(Path.Combine(_root, "launcher-config.json"), json);
        FooterStatus.Text = "Pasta do jogo salva.";
        Log($"Pasta selecionada: {folder}");
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!IsValidFolder(_gameFolder))
        {
            MessageBox.Show(this, "Escolha primeiro a pasta correta do jogo.", "Pasta não configurada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var script = Path.Combine(_root, "offline_launcher.py");
        if (!File.Exists(script)) { Log("offline_launcher.py não encontrado na raiz da distribuição."); return; }
        var python = new[] { Path.Combine(_root, "Launcher", "Runtime", "Python", "python.exe"), Path.Combine(_root, "Runtime", "Python", "python.exe"), Path.Combine(_root, "python.exe") }.FirstOrDefault(File.Exists);
        if (python is null) { Log("Python portátil não encontrado. Prepare o runtime antes de iniciar."); return; }
        var psi = new ProcessStartInfo(python) { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-u"); psi.ArgumentList.Add(script);
        try { Process.Start(psi); Log("Inicialização offline solicitada."); FooterStatus.Text = "Inicialização enviada ao offline_launcher.py."; }
        catch (Exception ex) { Log($"Falha ao iniciar: {ex.Message}"); }
    }

    private void Log(string message) => LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    private void Home_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Página inicial.";
    private void Install_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Serviços de instalação e reparo disponíveis; validar pacotes reais antes do uso.";
    private void Multiplayer_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Multiplayer experimental: utilize o overlay e os scripts existentes.";
    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var checks = new[] { ("ufo.exe", _gameFolder is not null && File.Exists(Path.Combine(_gameFolder, "ufo.exe"))),
            ("ufo_Data\\Managed", _gameFolder is not null && Directory.Exists(Path.Combine(_gameFolder, "ufo_Data", "Managed"))),
            ("offline_launcher.py", File.Exists(Path.Combine(_root, "offline_launcher.py"))),
            ("Python portátil", File.Exists(Path.Combine(_root, "Launcher", "Runtime", "Python", "python.exe")) || File.Exists(Path.Combine(_root, "Runtime", "Python", "python.exe")) || File.Exists(Path.Combine(_root, "python.exe"))) };
        foreach (var item in checks) Log($"{(item.Item2 ? "OK" : "FALHA")} — {item.Item1}");
        FooterStatus.Text = $"Diagnóstico básico concluído: {checks.Count(x => x.Item2)}/{checks.Length} verificações aprovadas.";
    }
    private void Updates_Click(object sender, RoutedEventArgs e) => FooterStatus.Text = "Atualizador requer manifesto HTTPS configurado pelo mantenedor.";
}