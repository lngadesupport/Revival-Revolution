using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace RevivalRevolution.Services;

public sealed record GameSettingsDiscoveryResult(
    bool InstallationFound,
    string Summary,
    IReadOnlyList<string> CandidateFiles,
    IReadOnlyList<string> RegistryLocations,
    IReadOnlyList<string> RelevantKeys);

/// <summary>
/// Read-only discovery of likely Unity/game preference locations.
/// It deliberately does not write configuration or infer that a discovered key is writable.
/// </summary>
public sealed class GameSettingsDiscoveryService
{
    private static readonly string[] CandidateNames =
    {
        "boot.config", "settings.ini", "settings.json", "config.ini", "config.json",
        "graphics.cfg", "video.cfg", "GameUserSettings.ini", "PlayerPrefs"
    };

    private static readonly string[] RelevantTokens =
    {
        "screenmanager", "resolution", "fullscreen", "quality", "vsync", "targetframerate",
        "framerate", "fps", "volume", "sensitivity", "deadzone", "controller", "graphics"
    };

    private const int MaximumTextFileBytes = 512 * 1024;

    public GameSettingsDiscoveryResult Inspect(string? gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return new(false, "Pasta do jogo não encontrada. Selecione uma instalação válida.",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var executable = Path.Combine(gameFolder, "ufo.exe");
        var dataDirectory = Path.Combine(gameFolder, "ufo_Data");
        if (!File.Exists(executable) || !Directory.Exists(dataDirectory))
            return new(false, "A instalação não contém ufo.exe e ufo_Data.",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var directories = new[]
        {
            gameFolder,
            dataDirectory,
            Path.Combine(dataDirectory, "StreamingAssets")
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        var files = new List<string>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Take(300).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var file in entries)
            {
                var name = Path.GetFileName(file);
                var extension = Path.GetExtension(file);
                if (!CandidateNames.Contains(name, StringComparer.OrdinalIgnoreCase) &&
                    !new[] { ".ini", ".cfg", ".json", ".xml", ".yaml", ".yml", ".toml" }
                        .Contains(extension, StringComparer.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > MaximumTextFileBytes) continue;
                    var text = File.ReadAllText(file);
                    var foundTokens = RelevantTokens.Where(token =>
                        text.Contains(token, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (CandidateNames.Contains(name, StringComparer.OrdinalIgnoreCase) || foundTokens.Length > 0)
                    {
                        files.Add(file);
                        foreach (var token in foundTokens) keys.Add(token);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    // An unreadable candidate is omitted; discovery remains read-only and best-effort.
                }
            }
        }

        var registryLocations = new List<string>();
        try
        {
            var metadata = FileVersionInfo.GetVersionInfo(executable);
            var company = CleanRegistrySegment(metadata.CompanyName);
            var product = CleanRegistrySegment(metadata.ProductName);
            if (!string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(product))
            {
                var registryPath = $@"Software\{company}\{product}";
                using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
                if (key is not null)
                {
                    registryLocations.Add(@"HKCU\" + registryPath);
                    foreach (var valueName in key.GetValueNames())
                        if (RelevantTokens.Any(token => valueName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                            keys.Add(valueName);
                }

                var localLow = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", company, product);
                if (Directory.Exists(localLow))
                {
                    registryLocations.Add(localLow);
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(localLow, "*", SearchOption.TopDirectoryOnly).Take(100))
                        {
                            var name = Path.GetFileName(file);
                            if (CandidateNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                                RelevantTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                                files.Add(file);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            // Registry metadata is optional; never block launcher startup on it.
        }

        var uniqueFiles = files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var uniqueLocations = registryLocations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var relevantKeys = keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var summary = uniqueFiles.Length == 0 && uniqueLocations.Length == 0
            ? "Nenhum arquivo de configuração conhecido foi identificado nos locais examinados. Nenhuma alteração foi feita."
            : $"Encontrados {uniqueFiles.Length} arquivo(s) candidato(s) e {uniqueLocations.Length} local(is) Unity/registro. A análise foi somente leitura.";
        return new(true, summary, uniqueFiles, uniqueLocations, relevantKeys);
    }

    private static string CleanRegistrySegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var cleaned = new string(value.Trim().Where(ch => !char.IsControl(ch) && ch != '\\' && ch != '/').ToArray());
        return cleaned.Trim();
    }
}
