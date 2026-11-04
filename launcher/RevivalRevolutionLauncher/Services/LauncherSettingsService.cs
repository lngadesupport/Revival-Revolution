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

    public string GetSettingsPath() => SettingsPath;
    public string GetLogDirectory() => Path.Combine(SettingsDirectory, "Logs");
}
