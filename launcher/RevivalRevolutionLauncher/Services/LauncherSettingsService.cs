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
    public bool VideoFullscreen { get; set; } = true;
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
            return Normalize(JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new LauncherSettings());
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
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
            File.Move(temporary, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static LauncherSettings Normalize(LauncherSettings value)
    {
        value.PlayerName = (value.PlayerName ?? "").Trim();
        if (value.PlayerName.Length > 24) value.PlayerName = value.PlayerName[..24];
        value.PlayerTag = (value.PlayerTag ?? "").Trim();
        if (value.PlayerTag.Length > 12) value.PlayerTag = value.PlayerTag[..12];

        value.ControllerLeftDeadZone = Math.Clamp(value.ControllerLeftDeadZone, 0, 50);
        value.ControllerRightDeadZone = Math.Clamp(value.ControllerRightDeadZone, 0, 50);
        value.ControllerTriggerDeadZone = Math.Clamp(value.ControllerTriggerDeadZone, 0, 50);
        value.ControllerHorizontalSensitivity = Math.Clamp(value.ControllerHorizontalSensitivity, 10, 200);
        value.ControllerVerticalSensitivity = Math.Clamp(value.ControllerVerticalSensitivity, 10, 200);
        value.ControllerLeftCurve = OneOf(value.ControllerLeftCurve, "Linear", "Suave", "Agressiva");
        value.ControllerRightCurve = OneOf(value.ControllerRightCurve, "Linear", "Suave", "Agressiva");

        value.VideoResolution = OneOf(value.VideoResolution, "Padrão do jogo", "1280 × 720",
            "1600 × 900", "1920 × 1080", "2560 × 1440", "3840 × 2160");
        value.GraphicsPreset = OneOf(value.GraphicsPreset, "Padrão do jogo", "Baixa", "Média", "Alta", "Personalizada");
        if (value.FpsLimit is not (60 or 75 or 90 or 120 or 144 or 165 or 180 or 200 or 240))
            value.FpsLimit = 60;

        value.AudioMasterVolume = Math.Clamp(value.AudioMasterVolume, 0, 100);
        value.AudioMusicVolume = Math.Clamp(value.AudioMusicVolume, 0, 100);
        value.AudioEffectsVolume = Math.Clamp(value.AudioEffectsVolume, 0, 100);
        value.SubtitleSize = OneOf(value.SubtitleSize, "Pequeno", "Médio", "Grande", "Muito grande");
        value.SubtitleColor = OneOf(value.SubtitleColor, "Branco", "Amarelo", "Ciano", "Verde");
        value.SubtitleBackground = OneOf(value.SubtitleBackground, "Transparente", "Escuro", "Preto");
        value.SubtitlePosition = OneOf(value.SubtitlePosition, "Inferior", "Superior");
        return value;
    }

    private static string OneOf(string? value, params string[] allowed) =>
        allowed.FirstOrDefault(option => string.Equals(option, value, StringComparison.Ordinal)) ?? allowed[0];

    public string GetSettingsPath() => SettingsPath;
    public string GetLogDirectory() => Path.Combine(SettingsDirectory, "Logs");
}
