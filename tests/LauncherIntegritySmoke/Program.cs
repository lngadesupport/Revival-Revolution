using System.Security.Cryptography;
using System.Text;
using RevivalRevolution.Services;

static string Hash(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var root = Path.Combine(Path.GetTempPath(), "RevivalLauncherIntegrity-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    await File.WriteAllTextAsync(Path.Combine(root, "valid.txt"), "expected");
    await File.WriteAllTextAsync(Path.Combine(root, "corrupt.txt"), "modified");

    var files = new[] { "valid.txt", "corrupt.txt", "missing.txt" };
    var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["valid.txt"] = Hash("expected"),
        ["corrupt.txt"] = Hash("expected"),
        ["missing.txt"] = Hash("new file")
    };

    var changed = await LauncherUpdateService.FindFilesNeedingUpdateAsync(root, files, hashes);
    Require(changed.Count == 2, "Expected exactly two files to be repaired.");
    Require(changed.Contains("corrupt.txt") && changed.Contains("missing.txt"),
        "The planner did not select only the modified and missing files.");
    Require(!changed.Contains("valid.txt"), "An intact file was incorrectly selected.");

    var staging = Path.Combine(root, "staging");
    Directory.CreateDirectory(staging);
    await File.WriteAllTextAsync(Path.Combine(staging, "corrupt.txt"), "expected");
    await File.WriteAllTextAsync(Path.Combine(staging, "missing.txt"), "new file");
    LauncherUpdateService.VerifyStagedFiles(staging, new[] { "corrupt.txt", "missing.txt" }, hashes);

    var applyMethod = typeof(LauncherUpdateService).GetMethod(
        "CreateApplyScriptAsync",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    Require(applyMethod is not null, "The transactional update script builder is unavailable.");
    var applyTask = (Task<string>)applyMethod!.Invoke(null, new object[]
    {
        root,
        staging,
        new[] { "corrupt.txt" },
        new Dictionary<string, string> { ["corrupt.txt"] = Hash("expected") },
        CancellationToken.None
    })!;
    var commandPath = await applyTask;
    var applyScript = await File.ReadAllTextAsync(Path.Combine(root, "Launcher", "apply-update.ps1"));
    var applyCommand = await File.ReadAllTextAsync(commandPath);
    Require(applyScript.Contains("Get-FileHash", StringComparison.Ordinal) &&
            applyScript.Contains(Hash("expected"), StringComparison.Ordinal) &&
            applyScript.Contains("Staged file changed before copy", StringComparison.Ordinal),
        "The apply script must reverify staged file hashes immediately before installation.");
    Require(applyScript.Contains("Assert-NoReparsePath", StringComparison.Ordinal),
        "The apply script must reject reparse points at apply time.");
    Require(applyCommand.Contains("-File", StringComparison.Ordinal) &&
            !applyCommand.Contains("-EncodedCommand", StringComparison.Ordinal),
        "The updater must launch a script file rather than exceed the Windows command-line limit.");

    await File.WriteAllTextAsync(Path.Combine(staging, "corrupt.txt"), "tampered after download");
    var stagedTamperRejected = false;
    try
    {
        LauncherUpdateService.VerifyStagedFiles(staging, new[] { "corrupt.txt" }, hashes);
    }
    catch (InvalidDataException)
    {
        stagedTamperRejected = true;
    }
    Require(stagedTamperRejected, "The staging verifier must reject a modified extracted file.");

    var stagedMissingRejected = false;
    try
    {
        LauncherUpdateService.VerifyStagedFiles(staging, new[] { "absent.txt" },
            new Dictionary<string, string> { ["absent.txt"] = Hash("absent") });
    }
    catch (InvalidDataException)
    {
        stagedMissingRejected = true;
    }
    Require(stagedMissingRejected, "The staging verifier must reject missing extracted files.");

    var managedRuntime = await LauncherUpdateService.FindFilesNeedingUpdateAsync(
        root,
        new[] { "offline_launcher.py", "inject_offline.py", "inject_overlay.py" },
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["offline_launcher.py"] = Hash("offline"),
            ["inject_offline.py"] = Hash("inject"),
            ["inject_overlay.py"] = Hash("overlay")
        });
    Require(managedRuntime.Count == 3,
        "Official launcher runtime scripts must remain eligible for versioned updates.");

    var localConfigRejected = false;
    try
    {
        await LauncherUpdateService.FindFilesNeedingUpdateAsync(
            root,
            new[] { "launcher-config.json" },
            new Dictionary<string, string> { ["launcher-config.json"] = Hash("config") });
    }
    catch (InvalidDataException)
    {
        localConfigRejected = true;
    }
    Require(localConfigRejected, "User-specific launcher configuration must remain protected.");
    foreach (var userContentPath in new[]
    {
        "Launcher/Mods/mobile-port/character.bundle",
        "Launcher/UserContent/custom-roster.json",
        "Launcher/Content/imported-assets/pack.bin",
        "Launcher/Profiles/community-profile.json",
        "Launcher/Backups/original-assembly.dll"
    })
    {
        var userContentRejected = false;
        try
        {
            await LauncherUpdateService.FindFilesNeedingUpdateAsync(
                root,
                new[] { userContentPath },
                new Dictionary<string, string> { [userContentPath] = Hash("must preserve") });
        }
        catch (InvalidDataException)
        {
            userContentRejected = true;
        }
        Require(userContentRejected, $"User-managed content must remain outside official update ownership: {userContentPath}");
    }


    var protectedRejected = false;
    try
    {
        await LauncherUpdateService.FindFilesNeedingUpdateAsync(
            root,
            new[] { "ufo_Data/Managed/Assembly-CSharp.dll" },
            new Dictionary<string, string> { ["ufo_Data/Managed/Assembly-CSharp.dll"] = Hash("blocked") });
    }
    catch (InvalidDataException)
    {
        protectedRejected = true;
    }
    Require(protectedRejected, "The integrity planner must refuse to repair game files.");

    var invalidHashRejected = false;
    try
    {
        await LauncherUpdateService.FindFilesNeedingUpdateAsync(
            root,
            new[] { "valid.txt" },
            new Dictionary<string, string> { ["valid.txt"] = "not-a-sha256" });
    }
    catch (InvalidDataException)
    {
        invalidHashRejected = true;
    }
    Require(invalidHashRejected, "The integrity planner must reject malformed SHA-256 values.");

    var validRadminCode = "MR1-DLRDSEZCHYAACAQDAQCQMBYIBEFAWDANBYHZGMDC2A";
    var decodedPeer = OnlineGameSessionService.DecodeRadminConnectionCode(validRadminCode);
    Require(decodedPeer.Address.ToString() == "26.226.57.19" && decodedPeer.Port == 8766,
        "The Radmin connection code decoder returned an unexpected endpoint.");

    var corruptCodeRejected = false;
    try
    {
        OnlineGameSessionService.DecodeRadminConnectionCode(validRadminCode[..^1] + "B");
    }
    catch (InvalidOperationException)
    {
        corruptCodeRejected = true;
    }
    Require(corruptCodeRejected, "The Radmin connection code decoder must reject a tampered checksum.");

    var nonRadminCode = "MR1-ZMAHCCTJQ4AACAQDAQCQMBYIBEFAWDANBYH53IXNME";
    var nonRadminRejected = false;
    try
    {
        OnlineGameSessionService.DecodeRadminConnectionCode(nonRadminCode);
    }
    catch (InvalidOperationException)
    {
        nonRadminRejected = true;
    }
    Require(nonRadminRejected, "The Radmin launcher must reject public/non-Radmin endpoints.");

    Require(!new LauncherSettings().UseCustomRadminIp, "Manual Radmin IP must be locked by default.");
    var settingsRoot = Path.Combine(root, "settings-test");
    var settingsService = new LauncherSettingsService(settingsRoot);
    settingsService.Save(new LauncherSettings
    {
        PlayerName = "  " + new string('P', 40) + "  ",
        PlayerTag = new string('T', 20),
        CheckUpdatesOnStartup = false,
        ConfirmBeforeLaunch = true,
        PersistLogs = false,
        UseCustomRadminIp = true,
        CustomRadminIp = "26.226.57.19"
    });
    var savedSettings = settingsService.Load();
    Require(savedSettings.PlayerName.Length == 24 && savedSettings.PlayerTag.Length == 12,
        "Legacy profile fields must be trimmed and bounded during persistence.");
    Require(!savedSettings.CheckUpdatesOnStartup && savedSettings.ConfirmBeforeLaunch && !savedSettings.PersistLogs,
        "Launcher preferences did not survive a save/load round trip.");
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
    Require(recoveredSettings.CheckUpdatesOnStartup && !recoveredSettings.ConfirmBeforeLaunch && recoveredSettings.PersistLogs,
        "Corrupt settings must recover to safe defaults.");
    Require(Directory.EnumerateFiles(settingsRoot, "launcher-settings.json.corrupt-*").Any(),
        "Corrupt settings must be preserved in a recovery copy.");
    settingsService.Save(new LauncherSettings { CheckUpdatesOnStartup = false });
    Require(!settingsService.Load().CheckUpdatesOnStartup,
        "Settings storage must remain writable after corruption recovery.");

    Console.WriteLine("Launcher/network integrity smoke tests passed: selective repair, staged and apply-time per-file SHA-256 verification, reparse-point protection, managed runtime updates, protected user configuration/game paths, and connection-code validation, resilient settings persistence and corruption recovery.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
