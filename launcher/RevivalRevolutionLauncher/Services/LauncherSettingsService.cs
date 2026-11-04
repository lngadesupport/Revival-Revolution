using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed class LauncherSettings
{
    public string PlayerName { get; set; } = "";
    public string PlayerTag { get; set; } = "";
    public bool ConfirmBeforeLaunch { get; set; } = false;
    public bool PersistLogs { get; set; } = true;
    public bool UseCustomRadminIp { get; set; } = false;
    public string CustomRadminIp { get; set; } = "";
}

public sealed class LauncherSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsDirectory;
    private string SettingsDirectory => _settingsDirectory;
    private string SettingsPath => Path.Combine(SettingsDirectory, "launcher-settings.json");

    public LauncherSettingsService(string? settingsDirectory = null)
    {
        _settingsDirectory = string.IsNullOrWhiteSpace(settingsDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevivalRevolution")
            : Path.GetFullPath(settingsDirectory);
    }

    public LauncherSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new LauncherSettings();

        try
        {
            // Allow the launcher UI and diagnostic readers to inspect the file
            // without making a transient sharing violation look like corruption.
            using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Normalize(JsonSerializer.Deserialize<LauncherSettings>(stream, JsonOptions)
                ?? new LauncherSettings());
        }
        catch (JsonException)
        {
            // Preserve the user's damaged file for recovery instead of silently
            // overwriting the only copy with defaults on the next save.
            TryArchiveCorruptSettings();
            return new LauncherSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);

        // Use a unique temporary file in the same directory so concurrent writes
        // cannot collide and the final rename stays on the same volume.
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string BoundedText(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static LauncherSettings Normalize(LauncherSettings settings) => new()
    {
        // Preserve bounded legacy profile values even though the current UI
        // no longer exposes them.
        PlayerName = BoundedText(settings.PlayerName, 24),
        PlayerTag = BoundedText(settings.PlayerTag, 12),
        ConfirmBeforeLaunch = settings.ConfirmBeforeLaunch,
        PersistLogs = settings.PersistLogs,
        UseCustomRadminIp = settings.UseCustomRadminIp,
        CustomRadminIp = BoundedText(settings.CustomRadminIp, 15)
    };

    private void TryArchiveCorruptSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var backup = SettingsPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") +
                "-" + Guid.NewGuid().ToString("N")[..8];
            File.Move(SettingsPath, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loading defaults remains possible even if the backup cannot be moved.
        }
    }

    public string GetSettingsPath() => SettingsPath;
    public string GetLogDirectory() => Path.Combine(SettingsDirectory, "Logs");
}
