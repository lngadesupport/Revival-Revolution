using System.IO.Compression;
using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed class GamePackageInstallerService
{
    private readonly DownloadService _downloads = new();
    private readonly ArchiveExtractionService _archives = new();
    private readonly IntegrityService _integrity = new();
    private static readonly (string Id,string File)[] Packages =
    {
        ("Xt8wLcy7","RevivalRevolution-Base.zip"),
        ("bfLGnQVJ","RevivalRevolution-Additional.zip")
    };

    public async Task InstallAsync(string root,IProgress<DownloadProgress>? progress,CancellationToken token)
    {
        var cache = Path.Combine(root,"Launcher","Downloads");
        Directory.CreateDirectory(cache);
        foreach (var package in Packages)
        {
            token.ThrowIfCancellationRequested();
            var zip = Path.Combine(cache,package.File);
            if (!IsZipValid(zip)) { if (File.Exists(zip)) File.Delete(zip); await _downloads.DownloadPixeldrainAsync(package.Id,zip,progress,token); }
            if (!IsZipValid(zip)) throw new InvalidDataException($"O pacote {package.File} não é um ZIP válido.");
            await _archives.ExtractZipSafelyAsync(zip,root,null,token);
        }
    }

    public async Task<IReadOnlyList<IntegrityResult>> VerifyAsync(string root,CancellationToken token)
        => await _integrity.VerifyAsync(root,Path.Combine(root,"SHA256SUMS.json"),token);

    public async Task RepairAsync(string root,IReadOnlyList<IntegrityResult> issues,CancellationToken token)
    {
        var paths = issues.Where(x => !x.IsValid).Select(x => x.Path.Replace('\\','/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (paths.Count == 0) return;
        var cache = Path.Combine(root,"Launcher","Downloads");
        var repaired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in Packages)
        {
            var zip = Path.Combine(cache,package.File);
            if (!IsZipValid(zip)) { await _downloads.DownloadPixeldrainAsync(package.Id,zip,null,token); }
            using var archive = ZipFile.OpenRead(zip);
            var matching = archive.Entries.Select(e=>e.FullName.Replace('\\','/')).Where(paths.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (matching.Count > 0) { await _archives.ExtractSelectedAsync(zip,root,matching,null,token); repaired.UnionWith(matching); }
        }
        var missing = paths.Except(repaired,StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("O reparo seletivo não encontrou estes arquivos nos pacotes: "+string.Join(", ",missing));
        var after = await VerifyAsync(root,token);
        var failed = after.Where(x=>!x.IsValid).Select(x=>x.Path).ToArray();
        if (failed.Length > 0) throw new InvalidOperationException("Ainda há arquivos inválidos após o reparo: "+string.Join(", ",failed));
    }

    private static bool IsZipValid(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 22) return false;
        try { using var z=ZipFile.OpenRead(path); return z.Entries.Count > 0; } catch { return false; }
    }
}
