using System.IO.Compression;

namespace RevivalRevolution.Services;

public sealed class ArchiveExtractionService
{
    public Task ExtractZipSafelyAsync(string archivePath, string destinationDirectory,
        IProgress<(int Completed, int Total, string Current)>? progress, CancellationToken cancellationToken)
        => ExtractSelectedAsync(archivePath, destinationDirectory, null, progress, cancellationToken);

    public Task ExtractSelectedAsync(string archivePath, string destinationDirectory, IReadOnlySet<string>? selectedPaths,
        IProgress<(int Completed, int Total, string Current)>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var root = Path.GetFullPath(destinationDirectory);
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);
            var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name) &&
                (selectedPaths == null || selectedPaths.Contains(e.FullName.Replace('\\', '/')))).ToList();
            var done = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !target.Equals(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Caminho inseguro no ZIP: {entry.FullName}");
                if (string.IsNullOrEmpty(entry.Name))
                { if (selectedPaths == null) Directory.CreateDirectory(target); continue; }
                if (selectedPaths != null && !selectedPaths.Contains(entry.FullName.Replace('\\', '/'))) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                progress?.Report((++done, files.Count, entry.FullName));
            }
        }, cancellationToken);
    }
}
