using System.IO.Compression;
using System.Security.Cryptography;

namespace RevivalRevolution.Services;

public sealed class PythonRuntimeService
{
    // 3.13.17 is the final 3.13 maintenance release with official Windows binaries.
    public const string RuntimeVersion = "3.13.17";
    public const string RuntimeUrl = "https://www.python.org/ftp/python/3.13.17/python-3.13.17-embed-amd64.zip";
    private const string RuntimeSha256 = "9e35319bffedab8b36ad44d8f377cb4f1733bcec257a0b6d04d94f01486ddb38";
    private readonly DownloadService _downloads;
    private readonly ArchiveExtractionService _archives;
    public PythonRuntimeService(DownloadService? downloads = null, ArchiveExtractionService? archives = null)
    { _downloads = downloads ?? new(); _archives = archives ?? new(); }

    public string? FindRuntime(string root) => new[]
    {
        Path.Combine(root,"Launcher","Runtime","Python","python.exe"),
        Path.Combine(root,"Runtime","Python","python.exe"),
        Path.Combine(root,"python.exe")
    }.FirstOrDefault(File.Exists);

    public async Task<string> InstallAsync(string root, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        var zip = Path.Combine(root,"Launcher","Downloads",$"python-{RuntimeVersion}-embed-amd64.zip");
        var target = Path.Combine(root,"Launcher","Runtime","Python");
        await _downloads.DownloadAsync(RuntimeUrl,zip,progress,token);
        await using (var stream = File.OpenRead(zip))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
            if (!actual.Equals(RuntimeSha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O hash SHA-256 do runtime Python não confere com o arquivo oficial.");
        }
        var staging = target + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging,true);
        Directory.CreateDirectory(staging);
        await _archives.ExtractZipSafelyAsync(zip,staging,null,token);
        var pth = Path.Combine(staging,"python313._pth");
        if (!File.Exists(pth)) throw new InvalidDataException("O pacote Python não contém python313._pth.");
        var lines = await File.ReadAllLinesAsync(pth,token);
        var normalized = lines.Where(x => x.Trim() is not "." and not "import site").ToList();
        normalized.Add("."); normalized.Add("..\\..\\.."); normalized.Add("import site");
        await File.WriteAllLinesAsync(pth,normalized,token);
        var stagedExe = Path.Combine(staging,"python.exe");
        if (!File.Exists(stagedExe)) throw new InvalidDataException("python.exe não foi encontrado após a extração.");
        if (Directory.Exists(target)) Directory.Delete(target,true);
        Directory.Move(staging,target);
        var exe = Path.Combine(target,"python.exe");
        var legacy = Path.Combine(root,"python-runtime.txt");
        await File.WriteAllTextAsync(legacy,exe,token);
        var game = Path.Combine(root,"Metal Revolution");
        if (Directory.Exists(game)) await File.WriteAllTextAsync(Path.Combine(game,"python-runtime.txt"),exe,token);
        return exe;
    }
}
