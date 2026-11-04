using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed class LauncherSettings
{
    public string PlayerName { get; set; } = "";
    public string PlayerTag { get; set; } = "";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool ConfirmBeforeLaunch { get; set; } = false;
    public bool PersistLogs { get; set; } = true;

    // Controller preferences. These are persisted now; applying them to gameplay
    // requires a verified input integration with the game.
    public int ControllerLeftDeadZone { get; set; } = 8;
    public int ControllerRightDeadZone { get; set; } = 8;
    public int ControllerTriggerDeadZone { get; set; } = 5;
    public int ControllerHorizontalSensitivity { get; set; } = 100;
    public int ControllerVerticalSensitivity { get; set; } = 100;
    public string ControllerLeftCurve { get; set; } = "Linear";
    public string ControllerRightCurve { get; set; } = "Linear";
    public bool ControllerInvertHorizontal { get; set; }
    public bool ControllerInvertVertical { get; set; }
    public bool ControllerVibration { get; set; } = true;

    // Video preferences. No game files are modified by saving these values.
    public string VideoResolution { get; set; } = "Padrão do jogo";
    public string GraphicsPreset { get; set; } = "Padrão do jogo";
    public int FpsLimit { get; set; } = 60;
    public bool VideoFullscreen { get; set; }
    public bool VideoVSync { get; set; } = true;
    public bool VideoShowFps { get; set; }

    // Audio preferences.
    public bool AudioMuted { get; set; }
    public int AudioMasterVolume { get; set; } = 100;
    public int AudioMusicVolume { get; set; } = 100;
    public int AudioEffectsVolume { get; set; } = 100;

    // Subtitle presentation preferences. Dialogue data and timing are supplied by the game patch.
    public bool SubtitlesEnabled { get; set; } = true;
    public string SubtitleSize { get; set; } = "Médio";
    public string SubtitleColor { get; set; } = "Branco";
    public string SubtitleBackground { get; set; } = "Escuro";
    public string SubtitlePosition { get; set; } = "Inferior";
}

public sealed class LauncherSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevivalRevolution");
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
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }

    public string GetSettingsPath() => SettingsPath;
    public string GetLogDirectory() => Path.Combine(SettingsDirectory, "Logs");
}
