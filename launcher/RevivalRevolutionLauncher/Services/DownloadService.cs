using System.Net;
using System.Net.Http.Headers;

namespace RevivalRevolution.Services;

public sealed class DownloadProgress
{
    public string FileName { get; init; } = "";
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }
    public bool IsResuming { get; init; }
    public double? Percent => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : null;
}

public sealed class DownloadService
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        AllowAutoRedirect = true
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public Task DownloadAsync(string url, string destination, IProgress<DownloadProgress>? progress,
        CancellationToken token, IReadOnlySet<string>? allowedRedirectHosts = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A URL de download deve usar HTTPS.", nameof(url));
        return DownloadAsync(uri, destination, progress, token, allowedRedirectHosts);
    }

    public Task DownloadPixeldrainAsync(string fileId, string destination, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(fileId) || fileId.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Identificador Pixeldrain inválido.", nameof(fileId));
        return DownloadAsync(new Uri($"https://pixeldrain.com/api/file/{fileId}?download"), destination, progress, token, null);
    }

    private static async Task DownloadAsync(Uri uri, string destination, IProgress<DownloadProgress>? progress,
        CancellationToken token, IReadOnlySet<string>? allowedRedirectHosts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var partial = destination + ".download";
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps ||
            (allowedRedirectHosts is not null && !allowedRedirectHosts.Contains(finalUri.Host)))
            throw new InvalidOperationException("O servidor redirecionou o download para um destino HTTPS não autorizado.");

        if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partial);
            throw new IOException("O servidor rejeitou a retomada. O arquivo parcial foi descartado; tente novamente.");
        }

        response.EnsureSuccessStatusCode();
        var append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (append && response.Content.Headers.ContentRange?.From != offset)
            throw new IOException("O servidor respondeu com um intervalo diferente do solicitado.");
        if (!append) offset = 0;

        var total = append && response.Content.Headers.ContentRange?.Length is long fullLength
            ? fullLength : response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using (var output = new FileStream(partial, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
        {
            var buffer = new byte[1024 * 1024];
            var received = offset;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                received += read;
                progress?.Report(new() { FileName = Path.GetFileName(destination), BytesReceived = received, TotalBytes = total, IsResuming = append });
            }
            await output.FlushAsync(token);
        }

        if (total is long expected && new FileInfo(partial).Length != expected)
            throw new IOException("Download incompleto; execute novamente para retomar.");

        File.Move(partial, destination, true);
    }
}
