using System.IO.Compression;

namespace RevivalRevolution.Services;

public sealed class ArchiveExtractionService
{
    private const int MaxEntries = 100_000;
    private const long MaxEntryBytes = 4L * 1024 * 1024 * 1024;
    private const long MaxTotalBytes = 20L * 1024 * 1024 * 1024;

    public Task ExtractZipSafelyAsync(string archivePath, string destinationDirectory,
        IProgress<(int Completed, int Total, string Current)>? progress, CancellationToken token)
        => ExtractSelectedAsync(archivePath, destinationDirectory, null, progress, token);

    public Task ExtractSelectedAsync(string archivePath, string destinationDirectory, IReadOnlySet<string>? selectedPaths,
        IProgress<(int Completed, int Total, string Current)>? progress, CancellationToken token)
        => Task.Run(() => Extract(archivePath, destinationDirectory, selectedPaths, progress, token), token);

    private static void Extract(string archivePath, string destinationDirectory, IReadOnlySet<string>? selectedPaths,
        IProgress<(int Completed, int Total, string Current)>? progress, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("O ZIP excede o limite de entradas permitido.");

        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name) &&
            (selectedPaths is null || selectedPaths.Contains(Normalize(e.FullName)))).ToArray();
        var totalBytes = files.Sum(e => e.Length);
        if (files.Any(e => e.Length > MaxEntryBytes) || totalBytes > MaxTotalBytes)
            throw new InvalidDataException("O ZIP excede o limite de tamanho descompactado.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completed = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var relative = Normalize(entry.FullName);
            ValidateEntry(entry, relative);
            if (!string.IsNullOrEmpty(entry.Name) && (selectedPaths is null || selectedPaths.Contains(relative)))
            {
                if (!seen.Add(relative)) throw new InvalidDataException($"Entrada duplicada no ZIP: {relative}");
                var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Caminho inseguro no ZIP: {entry.FullName}");

                var directory = Path.GetDirectoryName(target)!;
                Directory.CreateDirectory(directory);
                EnsureNoReparsePoint(root, directory);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                progress?.Report((++completed, files.Length, relative));
            }
            else if (string.IsNullOrEmpty(entry.Name) && selectedPaths is null)
            {
                var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Diretório inseguro no ZIP: {entry.FullName}");
                Directory.CreateDirectory(target);
                EnsureNoReparsePoint(root, target);
            }
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static void ValidateEntry(ZipArchiveEntry entry, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') ||
            path.Split('/').Any(part => part.Length == 0 || part is "." or ".." ||
                part.EndsWith(' ') || part.EndsWith('.')))
            throw new InvalidDataException($"Caminho inseguro no ZIP: {entry.FullName}");

        var mode = (entry.ExternalAttributes >> 16) & 0xF000;
        if (mode == 0xA000)
            throw new InvalidDataException($"Links simbólicos não são permitidos no ZIP: {entry.FullName}");
    }

    private static void EnsureNoReparsePoint(string root, string directory)
    {
        var current = directory;
        while (current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Ponto de reanálise não permitido no destino: {current}");
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current)!;
        }
    }
}
