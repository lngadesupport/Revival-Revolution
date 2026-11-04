using RevivalRevolution.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var root = Path.Combine(Path.GetTempPath(), "RevivalLauncherSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var validRadminCode = "MR1-DLRDSEZCHYAACAQDAQCQMBYIBEFAWDANBYHZGMDC2A";
    var decodedPeer = OnlineGameSessionService.DecodeRadminConnectionCode(validRadminCode);
    Require(decodedPeer.Address.ToString() == "26.226.57.19" && decodedPeer.Port == 8766,
        "The Radmin connection code decoder returned an unexpected endpoint.");

    var corruptCodeRejected = false;
    try { OnlineGameSessionService.DecodeRadminConnectionCode(validRadminCode[..^1] + "B"); }
    catch (InvalidOperationException) { corruptCodeRejected = true; }
    Require(corruptCodeRejected, "The Radmin connection code decoder must reject a tampered checksum.");

    var nonRadminCode = "MR1-ZMAHCCTJQ4AACAQDAQCQMBYIBEFAWDANBYH53IXNME";
    var nonRadminRejected = false;
    try { OnlineGameSessionService.DecodeRadminConnectionCode(nonRadminCode); }
    catch (InvalidOperationException) { nonRadminRejected = true; }
    Require(nonRadminRejected, "The launcher must reject public/non-Radmin endpoints.");

    Require(!new LauncherSettings().UseCustomRadminIp, "Manual Radmin IP must be locked by default.");
    Require(typeof(LauncherSettings).GetProperty("CheckUpdatesOnStartup") is null,
        "The removed launcher updater preference must not remain in the settings model.");

    var settingsRoot = Path.Combine(root, "settings-test");
    var settingsService = new LauncherSettingsService(settingsRoot);
    settingsService.Save(new LauncherSettings
    {
        PlayerName = "  " + new string('P', 40) + "  ",
        PlayerTag = new string('T', 20),
        ConfirmBeforeLaunch = true,
        ReduceMotion = true,
        PersistLogs = false,
        UseCustomRadminIp = true,
        CustomRadminIp = "26.226.57.19"
    });
    var savedSettings = settingsService.Load();
    Require(savedSettings.PlayerName.Length == 24 && savedSettings.PlayerTag.Length == 12,
        "Legacy profile fields must be trimmed and bounded during persistence.");
    Require(savedSettings.ConfirmBeforeLaunch && savedSettings.ReduceMotion && !savedSettings.PersistLogs,
        "Launcher and accessibility preferences did not survive a save/load round trip.");
    Require(savedSettings.UseCustomRadminIp && savedSettings.CustomRadminIp == "26.226.57.19",
        "Manual Radmin IP preferences did not survive a save/load round trip.");

    Require(RankedApiClient.ValidateEndpoint("http://26.226.57.19:8765").Contains("26.226.57.19"),
        "A valid Radmin endpoint was rejected.");
    var publicHttpRejected = false;
    try { RankedApiClient.ValidateEndpoint("http://8.8.8.8:8765"); }
    catch (InvalidOperationException) { publicHttpRejected = true; }
    Require(publicHttpRejected, "Manual Radmin endpoint validation must reject public HTTP addresses.");

    await File.WriteAllTextAsync(settingsService.GetSettingsPath(), "{ invalid json");
    var recoveredSettings = settingsService.Load();
    Require(!recoveredSettings.ConfirmBeforeLaunch && recoveredSettings.PersistLogs &&
            !recoveredSettings.UseCustomRadminIp && !recoveredSettings.ReduceMotion,
        "Corrupt settings must recover to safe defaults.");
    Require(Directory.EnumerateFiles(settingsRoot, "launcher-settings.json.corrupt-*").Any(),
        "Corrupt settings must be preserved in a recovery copy.");
    settingsService.Save(new LauncherSettings { ConfirmBeforeLaunch = true });
    Require(settingsService.Load().ConfirmBeforeLaunch,
        "Settings storage must remain writable after corruption recovery.");

    Console.WriteLine("Launcher smoke tests passed: Radmin connection-code validation, manual-IP safe default, settings persistence and corruption recovery; launcher updater preference is absent.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
