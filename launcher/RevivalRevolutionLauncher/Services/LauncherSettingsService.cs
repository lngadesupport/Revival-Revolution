using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed class LauncherSettings
{
    public string PlayerName { get; set; } = "";
    public string PlayerTag { get; set; } = "";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool ConfirmBeforeLaunch { get; set; } = false;
    public bool PersistLogs { get; set; } = true;
}

public sealed class LauncherSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    // Keep settings and launcher logs beside the executable so the ZIP build remains portable.
    private static string SettingsDirectory => Path.Combine(AppContext.BaseDirectory, "Launcher", "Config");
    private static string SettingsPath => Path.Combine(SettingsDirectory, "launcher-settings.json");

    public LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new LauncherSettings();
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new LauncherSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }

    public string GetSettingsPath() => SettingsPath;
    public string GetLogDirectory() => Path.Combine(AppContext.BaseDirectory, "Launcher", "Logs");
}
