using System.Net.Http;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed record LauncherUpdateInfo(string Version,string PackageUrl,string Sha256,string ReleaseNotes,IReadOnlyList<string> Files);
public sealed class LauncherUpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    public async Task<LauncherUpdateInfo?> CheckAsync(string root,CancellationToken token=default)
    {
        var config=Path.Combine(root,"launcher-update.json");
        if(!File.Exists(config)) return null;
        using var cfg=JsonDocument.Parse(await File.ReadAllTextAsync(config,token));
        var url=cfg.RootElement.GetProperty("manifestUrl").GetString();
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host.Contains("SEU-DOMINIO",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configure manifestUrl HTTPS válido em launcher-update.json.");
        using var response=await Http.GetAsync(uri,token); response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var e=doc.RootElement;
        var files=e.GetProperty("files").EnumerateArray().Select(x=>x.GetString()??"").Where(x=>x.Length>0).ToArray();
        return new(e.GetProperty("version").GetString()??"0",e.GetProperty("packageUrl").GetString()??"",
            e.GetProperty("sha256").GetString()??"",e.TryGetProperty("releaseNotes",out var n)?n.GetString()??"": "",files);
    }

    public async Task<string> DownloadAndStageAsync(string root,LauncherUpdateInfo update,IProgress<DownloadProgress>? progress,CancellationToken token)
    {
        if(!Uri.TryCreate(update.PackageUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https") throw new InvalidOperationException("packageUrl deve usar HTTPS.");
        if(update.Sha256.Length!=64||!update.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("SHA-256 do pacote inválido.");
        var downloads=Path.Combine(root,"Launcher","Downloads"); Directory.CreateDirectory(downloads);
        var zip=Path.Combine(downloads,"launcher-update.zip");
        await DownloadAsync(uri.ToString(),zip,progress,token);
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
        foreach(var entry in archive.Entries.Where(x=>!string.IsNullOrEmpty(x.Name)))
        {
            token.ThrowIfCancellationRequested();
            var rel=entry.FullName.Replace('\\','/');
            if(!allowed.Contains(rel)) continue;
            if(rel.StartsWith('/')||rel.Split('/').Any(p=>p==".."||p==".")) throw new InvalidDataException("Caminho inseguro no pacote de atualização.");
            if(rel.StartsWith("Metal Revolution/",StringComparison.OrdinalIgnoreCase)||rel.StartsWith("ufo_Data/",StringComparison.OrdinalIgnoreCase)||rel.StartsWith("Launcher/Downloads/",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O atualizador não pode substituir arquivos do jogo/runtime/cache.");
            var target=Path.GetFullPath(Path.Combine(stage,rel.Replace('/',Path.DirectorySeparatorChar)));
            var prefix=Path.GetFullPath(stage)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Caminho de atualização fora da staging.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target,true);
        }
        var script=Path.Combine(root,"Launcher","apply-update.cmd");
        var lines=new List<string> { "@echo off","timeout /t 2 /nobreak >nul" };
        foreach(var rel in update.Files)
        {
            var source=Path.Combine(stage,rel.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(source)) { var dest=Path.Combine(root,rel.Replace('/',Path.DirectorySeparatorChar)); lines.Add($"if exist \"{source}\" copy /y \"{source}\" \"{dest}\" >nul"); }
        }
        lines.Add($"rmdir /s /q \"{stage}\""); lines.Add("del \"%~f0\"");
        await File.WriteAllLinesAsync(script,lines,token);
        return script;
    }

    private static async Task DownloadAsync(string url,string path,IProgress<DownloadProgress>? progress,CancellationToken token)
    {
        var part=path+".download"; var offset=File.Exists(part)?new FileInfo(part).Length:0L;
        using var req=new HttpRequestMessage(HttpMethod.Get,url); if(offset>0) req.Headers.Range=new(offset,null);
        using var res=await Http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
        if(offset>0&&res.StatusCode==System.Net.HttpStatusCode.RequestedRangeNotSatisfiable){File.Delete(part);offset=0;throw new IOException("Servidor rejeitou retomada; tente novamente.");}
        res.EnsureSuccessStatusCode(); var append=offset>0&&res.StatusCode==System.Net.HttpStatusCode.PartialContent;
        if(!append)offset=0;
        var total=append?res.Content.Headers.ContentRange?.Length:res.Content.Headers.ContentLength;
        await using var input=await res.Content.ReadAsStreamAsync(token);
        await using var output=new FileStream(part,append?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,81920,true);
        var buffer=new byte[81920];int read;long received=offset;
        while((read=await input.ReadAsync(buffer,token))>0){await output.WriteAsync(buffer.AsMemory(0,read),token);received+=read;progress?.Report(new(){FileName=Path.GetFileName(path),BytesReceived=received,TotalBytes=total,IsResuming=append});}
        await output.FlushAsync(token); output.Close();
        if(total is long expected&&new FileInfo(part).Length!=expected) throw new IOException("Download incompleto; a próxima tentativa poderá retomar.");
        File.Move(part,path,true);
    }
}
