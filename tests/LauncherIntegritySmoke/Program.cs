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

    Console.WriteLine("Launcher/network integrity smoke tests passed: selective repair, staged per-file SHA-256 verification, tamper rejection, protected game paths, and connection-code validation.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
