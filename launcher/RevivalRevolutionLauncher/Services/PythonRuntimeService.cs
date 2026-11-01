using System.IO.Compression;

namespace RevivalRevolution.Services;

public sealed class PythonRuntimeService
{
    public const string RuntimeUrl = "https://www.python.org/ftp/python/3.13.16/python-3.13.16-embed-amd64.zip";
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
        var zip = Path.Combine(root,"Launcher","Downloads","python-3.13.16-embed-amd64.zip");
        var target = Path.Combine(root,"Launcher","Runtime","Python");
        await _downloads.DownloadAsync(RuntimeUrl,zip,progress,token);
        if (Directory.Exists(target)) Directory.Delete(target,true);
        Directory.CreateDirectory(target);
        await _archives.ExtractZipSafelyAsync(zip,target,null,token);
        var pth = Path.Combine(target,"python313._pth");
        if (!File.Exists(pth)) throw new InvalidDataException("O pacote Python não contém python313._pth.");
        var lines = await File.ReadAllLinesAsync(pth,token);
        var normalized = lines.Where(x => x.Trim() is not "." and not "import site").ToList();
        normalized.Add("."); normalized.Add("..\\..\\.."); normalized.Add("import site");
        await File.WriteAllLinesAsync(pth,normalized,token);
        var exe = Path.Combine(target,"python.exe");
        if (!File.Exists(exe)) throw new InvalidDataException("python.exe não foi encontrado após a extração.");
        var legacy = Path.Combine(root,"python-runtime.txt");
        await File.WriteAllTextAsync(legacy,exe,token);
        var game = Path.Combine(root,"Metal Revolution");
        if (Directory.Exists(game)) await File.WriteAllTextAsync(Path.Combine(game,"python-runtime.txt"),exe,token);
        return exe;
    }
}
