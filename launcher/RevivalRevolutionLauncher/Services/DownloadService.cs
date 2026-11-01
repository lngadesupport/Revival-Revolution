using System.Net;
using System.Net.Http.Headers;

namespace RevivalRevolution.Services;

public sealed class DownloadProgress
{
    public string FileName { get; init; } = "";
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }
    public double? Percent => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : null;
    public bool IsResuming { get; init; }
}

public sealed class DownloadService
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.None
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task DownloadAsync(string url, string destination, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A URL de download deve usar HTTPS.", nameof(url));
        await DownloadHttpAsync(uri, destination, progress, cancellationToken);
    }

    private static async Task DownloadHttpAsync(Uri uri, string destination, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var partial = destination + ".download";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0L;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partial);
            throw new IOException("O servidor rejeitou a retomada; execute o download novamente.");
        }
        response.EnsureSuccessStatusCode();
        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append) existing = 0;
        var total = append && response.Content.Headers.ContentRange?.Length is long full ? full : response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var output = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
        {
            var buffer = new byte[1024 * 1024]; long received = existing; int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken); received += read;
                progress?.Report(new DownloadProgress { FileName = Path.GetFileName(destination), BytesReceived = received, TotalBytes = total, IsResuming = append });
            }
            await output.FlushAsync(cancellationToken);
        }
        if (total is long expected && new FileInfo(partial).Length != expected)
            throw new IOException("Download incompleto; execute novamente para retomar.");
        File.Move(partial, destination, true);
    }

    public async Task DownloadPixeldrainAsync(
        string fileId,
        string destination,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId) || fileId.Any(c => !char.IsLetterOrDigit(c)))
            throw new ArgumentException("Identificador Pixeldrain inválido.", nameof(fileId));

        var url = $"https://pixeldrain.com/api/file/{fileId}?download";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var partial = destination + ".download";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partial);
            await DownloadPixeldrainAsync(fileId, destination, progress, cancellationToken);
            return;
        }
        response.EnsureSuccessStatusCode();
        if (existing > 0 && !append) existing = 0;

        var responseLength = response.Content.Headers.ContentLength;
        var total = append && response.Content.Headers.ContentRange?.Length is long fullLength
            ? fullLength
            : responseLength;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var output = new FileStream(partial,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
        {
            var buffer = new byte[1024 * 1024];
            long received = existing;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress?.Report(new DownloadProgress
                {
                    FileName = Path.GetFileName(destination),
                    BytesReceived = received,
                    TotalBytes = total,
                    IsResuming = append
                });
            }
            await output.FlushAsync(cancellationToken);
        }

        var finalLength = new FileInfo(partial).Length;
        if (total is long expected && finalLength != expected)
            throw new IOException($"Download incompleto: recebidos {finalLength} de {expected} bytes. Execute novamente para continuar.");

        File.Move(partial, destination, true);
    }
}
