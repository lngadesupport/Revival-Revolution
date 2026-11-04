using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RevivalRevolution.Services;

static string Hash(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var legacySettings = JsonSerializer.Deserialize<LauncherSettings>("{\"PlayerName\":\"LegacyPlayer\",\"PlayerTag\":\"OLD\"}");
Require(legacySettings is not null, "Legacy launcher settings must deserialize.");
Require(legacySettings!.PlayerName == "LegacyPlayer", "Legacy player profile was not preserved.");
Require(legacySettings.FpsLimit == 60 && legacySettings.ControllerLeftDeadZone == 8,
    "New 0.8.4 settings must receive defaults when loading an older settings file.");
var currentSettings = new LauncherSettings
{
    FpsLimit = 144,
    ControllerLeftDeadZone = 12,
    SubtitlesEnabled = false,
    AudioMusicVolume = 65
};
var roundTrip = JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(currentSettings));
Require(roundTrip is not null && roundTrip.FpsLimit == 144 &&
    roundTrip.ControllerLeftDeadZone == 12 && !roundTrip.SubtitlesEnabled &&
    roundTrip.AudioMusicVolume == 65, "0.8.4 settings did not survive JSON round-trip.");
var defaultVideoOptions = UnityPlayerLaunchOptions.FromSettings(new LauncherSettings());
Require(defaultVideoOptions.Fullscreen == "1" && defaultVideoOptions.Width is null &&
    defaultVideoOptions.Height is null, "Default display settings must preserve the existing fullscreen launch.");
var customVideoOptions = UnityPlayerLaunchOptions.FromSettings(new LauncherSettings
{
    VideoFullscreen = false,
    VideoResolution = "1920 × 1080"
});
Require(customVideoOptions.Fullscreen == "0" && customVideoOptions.Width == "1920" &&
    customVideoOptions.Height == "1080", "Validated Unity resolution/fullscreen options were not generated.");
var unsupportedVideoOptions = UnityPlayerLaunchOptions.FromSettings(new LauncherSettings
{
    VideoResolution = "3440 × 1440"
});
Require(unsupportedVideoOptions.Width is null && unsupportedVideoOptions.Height is null,
    "Unsupported resolutions must not be passed to the game.");


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

    var gameRoot = Path.Combine(root, "Game");
    var gameData = Path.Combine(gameRoot, "ufo_Data");
    Directory.CreateDirectory(gameData);
    await File.WriteAllTextAsync(Path.Combine(gameRoot, "ufo.exe"), "test executable metadata placeholder");
    var bootConfig = Path.Combine(gameData, "boot.config");
    await File.WriteAllTextAsync(bootConfig, "gfx-enable-gfx-jobs=1\nvSyncCount=1\n");
    var beforeScan = Hash(await File.ReadAllTextAsync(bootConfig));
    var discovery = new GameSettingsDiscoveryService().Inspect(gameRoot);
    Require(discovery.InstallationFound, "Read-only discovery did not recognize a minimal Unity game layout.");
    Require(discovery.CandidateFiles.Contains(bootConfig), "Read-only discovery did not identify Unity boot.config.");
    Require(discovery.RelevantKeys.Contains("vsync"), "Read-only discovery did not recognize a relevant graphics token.");
    Require(Hash(await File.ReadAllTextAsync(bootConfig)) == beforeScan,
        "Read-only discovery unexpectedly modified a game configuration file.");
    var missingDiscovery = new GameSettingsDiscoveryService().Inspect(Path.Combine(root, "missing-game"));
    Require(!missingDiscovery.InstallationFound, "Discovery must report a missing game directory without throwing.");

    Console.WriteLine("Launcher smoke tests passed: legacy settings compatibility, JSON round-trip, selective repair, intact-file preservation, protected game paths, SHA-256 validation, and read-only Unity config discovery.");

}
finally
{
    Directory.Delete(root, recursive: true);
}
