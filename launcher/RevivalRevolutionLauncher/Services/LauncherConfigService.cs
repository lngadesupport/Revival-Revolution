using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed class LauncherConfigService
{
    private sealed class ConfigModel
    {
        public string? game_folder { get; set; }
    }

    private static string ConfigPath(string root) =>
        Path.Combine(root, "Launcher", "Config", "launcher-config.json");

    public string? GetConfiguredGameFolder(string root)
    {
        var candidates = new[]
        {
            ConfigPath(root),
            Path.Combine(root, "launcher-config.json"),
            Path.Combine(root, "Metal Revolution", "launcher-config.json")
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var model = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(path));
                var configured = model?.game_folder;
                if (string.IsNullOrWhiteSpace(configured)) continue;

                configured = Environment.ExpandEnvironmentVariables(configured);
                if (Path.IsPathRooted(configured) && Directory.Exists(configured))
                    return Path.GetFullPath(configured);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }

        var bundledGame = Path.Combine(root, "Metal Revolution");
        return Directory.Exists(bundledGame) ? bundledGame : null;
    }

    public void SaveGameFolder(string root, string folder, string? distributionRoot = null)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        var json = JsonSerializer.Serialize(
            new ConfigModel { game_folder = Path.GetFullPath(folder) },
            new JsonSerializerOptions { WriteIndented = true });

        WriteAtomic(ConfigPath(root), json);
        // The WPF launcher and legacy Python bootstrap may live in different directories.
        WriteAtomic(Path.Combine(root, "launcher-config.json"), json);
        if (!string.IsNullOrWhiteSpace(distributionRoot) &&
            !Path.GetFullPath(distributionRoot).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            WriteAtomic(Path.Combine(distributionRoot, "launcher-config.json"), json);
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }
}
