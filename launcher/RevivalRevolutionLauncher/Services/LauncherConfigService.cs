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
                if (!string.IsNullOrWhiteSpace(model?.game_folder))
                {
                    var configured = Environment.ExpandEnvironmentVariables(model.game_folder);
                    if (Path.IsPathRooted(configured) && Directory.Exists(configured))
                        return configured;
                }
            }
            catch
            {
                // A damaged or foreign-PC config is ignored; the user can select the folder.
            }
        }

        var bundledGame = Path.Combine(root, "Metal Revolution");
        return Directory.Exists(bundledGame) ? bundledGame : null;
    }

    public void SaveGameFolder(string root, string folder)
    {
        var directory = Path.GetDirectoryName(ConfigPath(root))!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(new ConfigModel { game_folder = folder },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath(root), json);

        // The existing Python bootstrap reads launcher-config.json from its own directory.
        // Keep that compatibility file updated while the new launcher owns the UI.
        var legacyPath = Path.Combine(root, "launcher-config.json");
        File.WriteAllText(legacyPath, json);
    }
}
