using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RevivalRevolution.Services;

/// <summary>
/// Best-effort downloader for the three public legacy bootstrap files in the configured
/// Google Drive folder. Files are staged and validated before they replace local copies.
/// </summary>
public sealed class GoogleDriveDistributionDownloader
{
    private const string FolderId = "1L14IrOOQh-HnNtMPH4Cyp-xA8WOlWFZC";
    private const int MaxFileBytes = 10 * 1024 * 1024;
    private static readonly string[] RequiredFiles =
    {
        "offline_launcher.py", "inject_offline.py", "inject_overlay.py"
    };

    public async Task<string> DownloadMissingAsync(string launcherRoot, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(Path.GetFullPath(launcherRoot), "OfflineScripts");
        Directory.CreateDirectory(destination);

        if (RequiredFiles.All(name => IsPlausibleScript(Path.Combine(destination, name))))
            return destination;

        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RevivalRevolutionLauncher/0.7.2");

        var folderUrl = $"https://drive.google.com/embeddedfolderview?id={FolderId}#list";
        using var listingResponse = await client.GetAsync(folderUrl, cancellationToken);
        listingResponse.EnsureSuccessStatusCode();
        var listingHtml = await listingResponse.Content.ReadAsStringAsync(cancellationToken);
        var fileIds = FindFileIds(listingHtml);

        var staging = Path.Combine(destination, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var name in RequiredFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = Path.Combine(destination, name);
                if (IsPlausibleScript(existing))
                    continue;

                if (!fileIds.TryGetValue(name, out var fileId))
                    throw new InvalidDataException($"O arquivo {name} não foi encontrado na pasta pública do Google Drive.");

                var bytes = await DownloadFileAsync(client, fileId, cancellationToken);
                ValidateScriptPayload(name, bytes);
                await File.WriteAllBytesAsync(Path.Combine(staging, name), bytes, cancellationToken);
            }

            foreach (var name in RequiredFiles)
            {
                var staged = Path.Combine(staging, name);
                if (File.Exists(staged))
                    File.Move(staged, Path.Combine(destination, name), overwrite: true);
            }

            if (!RequiredFiles.All(name => IsPlausibleScript(Path.Combine(destination, name))))
                throw new InvalidDataException("O conjunto de scripts baixado não passou na validação local.");

            return destination;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Dictionary<string, string> FindFileIds(string html)
    {
        var decoded = WebUtility.HtmlDecode(html);
        var matches = Regex.Matches(decoded, @"data-id=[""'](?<id>[A-Za-z0-9_-]{10,})[""']", RegexOptions.IgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var start = match.Index;
            var end = decoded.IndexOf("data-id=", start + match.Length, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = Math.Min(decoded.Length, start + 3000);
            var fragment = decoded[start..end];

            foreach (var name in RequiredFiles)
            {
                if (fragment.Contains(name, StringComparison.OrdinalIgnoreCase))
                    result[name] = match.Groups["id"].Value;
            }
        }

        // Some Drive folder responses place the title before the data-id attribute.
        foreach (var name in RequiredFiles)
        {
            if (result.ContainsKey(name)) continue;
            var nameIndex = decoded.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0) continue;
            var start = Math.Max(0, nameIndex - 1500);
            var fragment = decoded[start..Math.Min(decoded.Length, nameIndex + name.Length + 500)];
            var idMatch = Regex.Match(fragment, @"data-id=[""'](?<id>[A-Za-z0-9_-]{10,})[""']", RegexOptions.IgnoreCase);
            if (idMatch.Success) result[name] = idMatch.Groups["id"].Value;
        }

        return result;
    }

    private static async Task<byte[]> DownloadFileAsync(HttpClient client, string fileId, CancellationToken cancellationToken)
    {
        var url = $"https://drive.google.com/uc?export=download&id={Uri.EscapeDataString(fileId)}";
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
        if (LooksLikeHtml(bytes) || mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            var html = Encoding.UTF8.GetString(bytes);
            var confirm = Regex.Match(html, @"[?&]confirm=([0-9A-Za-z_-]+)", RegexOptions.IgnoreCase);
            if (!confirm.Success)
                confirm = Regex.Match(html, @"name=[""']confirm[""'][^>]*value=[""'](?<token>[0-9A-Za-z_-]+)[""']", RegexOptions.IgnoreCase);
            if (!confirm.Success)
                confirm = Regex.Match(html, @"value=[""'](?<token>[0-9A-Za-z_-]+)[""'][^>]*name=[""']confirm[""']", RegexOptions.IgnoreCase);
            var token = confirm.Groups["token"].Success ? confirm.Groups["token"].Value : confirm.Groups[1].Value;
            if (!confirm.Success || string.IsNullOrWhiteSpace(token))
                throw new InvalidDataException("O Google Drive não forneceu o arquivo diretamente (pode exigir acesso público ou confirmação).");

            var confirmedUrl = $"https://drive.google.com/uc?export=download&confirm={Uri.EscapeDataString(token)}&id={Uri.EscapeDataString(fileId)}";
            using var confirmedResponse = await client.GetAsync(confirmedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            confirmedResponse.EnsureSuccessStatusCode();
            bytes = await ReadBoundedAsync(confirmedResponse.Content, cancellationToken);
        }

        return bytes;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > MaxFileBytes)
            throw new InvalidDataException("O arquivo do Google Drive excede o limite de 10 MB.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaxFileBytes)
                throw new InvalidDataException("O arquivo do Google Drive excede o limite de 10 MB.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void ValidateScriptPayload(string name, byte[] bytes)
    {
        if (bytes.Length == 0 || LooksLikeHtml(bytes))
            throw new InvalidDataException($"O download de {name} retornou conteúdo vazio ou uma página HTML, não um script Python.");

        var text = Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 4096))).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (text.Length == 0 || text.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"O conteúdo de {name} não parece ser um script Python.");
    }

    private static bool IsPlausibleScript(string path)
    {
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > MaxFileBytes) return false;
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(512, (int)info.Length)];
            _ = stream.Read(buffer, 0, buffer.Length);
            return !LooksLikeHtml(buffer);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool LooksLikeHtml(byte[] bytes)
    {
        var prefix = Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 512)))
            .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return prefix.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
               prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
               prefix.StartsWith("<head", StringComparison.OrdinalIgnoreCase) ||
               prefix.StartsWith("<form", StringComparison.OrdinalIgnoreCase);
    }
}
