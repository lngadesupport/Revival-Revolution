using System.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed record LauncherUpdateInfo(string Version,string PackageUrl,string Sha256,string ReleaseNotes,IReadOnlyList<string> Files);
public sealed class LauncherUpdateService
{
    private readonly DownloadService _downloads = new();
    private static readonly HttpClient ManifestHttp = new() { Timeout = TimeSpan.FromMinutes(2) };

    private static async Task<HttpResponseMessage> GetManifestAsync(Uri uri, CancellationToken token)
    {
        var response = await ManifestHttp.GetAsync(uri, token);
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
        {
            response.Dispose();
            throw new InvalidOperationException("O manifesto foi redirecionado para uma conexão sem HTTPS.");
        }
        response.EnsureSuccessStatusCode();
        return response;
    }

    public async Task<LauncherUpdateInfo?> CheckAsync(string root,CancellationToken token=default)
    {
        var config=Path.Combine(root,"launcher-update.json");
        if(!File.Exists(config)) return null;
        using var cfg=JsonDocument.Parse(await File.ReadAllTextAsync(config,token));
        var url=cfg.RootElement.GetProperty("manifestUrl").GetString();
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host.Contains("SEU-DOMINIO",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configure manifestUrl HTTPS válido em launcher-update.json.");
        using var response=await GetManifestAsync(uri,token);
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var e=doc.RootElement;
        var versionText=e.GetProperty("version").GetString()??"0";
        if(!Version.TryParse(versionText,out var remoteVersion))
            throw new InvalidDataException("A versão remota do launcher não é válida.");
        var localVersion=typeof(LauncherUpdateService).Assembly.GetName().Version??new Version(0,0);
        if(remoteVersion<=localVersion) return null;
        var files=e.GetProperty("files").EnumerateArray().Select(x=>x.GetString()??"").Where(x=>x.Length>0).ToArray();
        return new(versionText,e.GetProperty("packageUrl").GetString()??"",
            e.GetProperty("sha256").GetString()??"",e.TryGetProperty("releaseNotes",out var n)?n.GetString()??"": "",files);
    }

    public async Task<string> DownloadAndStageAsync(string root,LauncherUpdateInfo update,IProgress<DownloadProgress>? progress,CancellationToken token)
    {
        if(!Uri.TryCreate(update.PackageUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https") throw new InvalidOperationException("packageUrl deve usar HTTPS.");
        if(update.Sha256.Length!=64||!update.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("SHA-256 do pacote inválido.");
        var downloads=Path.Combine(root,"Launcher","Downloads"); Directory.CreateDirectory(downloads);
        var zip=Path.Combine(downloads,"launcher-update.zip");
        await _downloads.DownloadAsync(uri.ToString(), zip, progress, token);
        await using(var s=File.OpenRead(zip))
        {
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(s,token));
            if(!hash.Equals(update.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Hash SHA-256 do pacote de atualização não confere.");
        }
        var stage=Path.Combine(root,"Launcher","UpdateStaging");
        if(Directory.Exists(stage)) Directory.Delete(stage,true);
        Directory.CreateDirectory(stage);
        using var archive=ZipFile.OpenRead(zip);
        var allowed=update.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var rel in allowed) ValidateRelativePath(rel);
        if (archive.Entries.Count > 50_000 || archive.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).Sum(x => x.Length) > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("O pacote de atualização excede os limites permitidos.");
        var extracted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in archive.Entries.Where(x=>!string.IsNullOrEmpty(x.Name)))
        {
            token.ThrowIfCancellationRequested();
            var rel=entry.FullName.Replace('\\','/');
            ValidateRelativePath(rel);
            if(!allowed.Contains(rel)) continue;
            if (!extracted.Add(rel)) throw new InvalidDataException($"Arquivo duplicado no pacote de atualização: {rel}");
            if(IsProtectedPath(rel))
                throw new InvalidDataException("O atualizador não pode substituir arquivos do jogo/runtime/cache.");
            var target=Path.GetFullPath(Path.Combine(stage,rel.Replace('/',Path.DirectorySeparatorChar)));
            var prefix=Path.GetFullPath(stage)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Caminho de atualização fora da staging.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, false);
        }
        if(extracted.Count==0) throw new InvalidDataException("O pacote não contém nenhum arquivo autorizado pelo manifesto.");
        foreach(var rel in allowed)
            if(!extracted.Contains(rel)) throw new InvalidDataException($"Arquivo listado no manifesto não encontrado no pacote: {rel}");

        static string PsQuote(string value) => "'" + value.Replace("'","''") + "'";
        var ps=new StringBuilder();
        foreach(var rel in extracted)
        {
            var source=Path.Combine(stage,rel.Replace('/',Path.DirectorySeparatorChar));
            var dest=Path.Combine(root,rel.Replace('/',Path.DirectorySeparatorChar));
            ps.AppendLine($"$src={PsQuote(source)}; $dst={PsQuote(dest)}; New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null; Copy-Item -LiteralPath $src -Destination $dst -Force");
        }
        ps.AppendLine($"Remove-Item -LiteralPath {PsQuote(stage)} -Recurse -Force -ErrorAction SilentlyContinue");
        var encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(ps.ToString()));
        var script=Path.Combine(root,"Launcher","apply-update.cmd");
        await File.WriteAllTextAsync(script,"@echo off\r\ntimeout /t 2 /nobreak >nul\r\npowershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "+encoded+"\r\ndel \"%~f0\"\r\n",token);
        return script;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Caminho vazio no manifesto/pacote.");
        var normalized = path.Replace('\\', '/');
        var parts = normalized.Split('/');
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (normalized.StartsWith('/') || normalized.Contains(':') ||
            parts.Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(c => "<>:\"|?*".Contains(c)) || reserved.Contains(part.Split('.')[0])))
            throw new InvalidDataException($"Caminho inseguro no pacote de atualização: {path}");
    }

    private static bool IsProtectedPath(string rel)
    {
        var path = rel.Replace('\\', '/');
        return path.StartsWith("Metal Revolution/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("ufo_Data/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Launcher/Downloads/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Launcher/Runtime/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Launcher/UpdateStaging/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Launcher/Config/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Launcher/Logs/", StringComparison.OrdinalIgnoreCase) ||
            new[] { "launcher-update.json", "launcher-config.json", "python-runtime.txt", "autopunch-config.json",
                "offline_launcher.py", "inject_offline.py", "inject_overlay.py", "MetalNetplayBridge.cs",
                "MetalOfflineBridgeImGui.cs", "SHA256SUMS.json" }.Contains(path, StringComparer.OrdinalIgnoreCase);
    }

}
