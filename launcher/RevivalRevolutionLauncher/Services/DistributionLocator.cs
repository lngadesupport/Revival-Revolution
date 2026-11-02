namespace RevivalRevolution.Services;

/// <summary>
/// Finds the legacy offline bootstrap even when the launcher and the game are installed
/// in sibling folders or the distribution is nested one or two levels below its root.
/// </summary>
public sealed class DistributionLocator
{
    private static readonly string[] RequiredBootstrapFiles =
    {
        "offline_launcher.py", "inject_offline.py", "inject_overlay.py"
    };

    private static readonly string[] SearchFolderNames =
    {
        "Distribution", "distribution", "Scripts", "scripts", "Launcher", "launcher",
        "Offline", "offline", "OfflineScripts", "Bootstrap", "bootstrap"
    };

    public string? Find(string launcherRoot, string? gameFolder)
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            var game = Path.GetFullPath(gameFolder);

            // Prefer the selected game installation so its original scripts can be
            // synchronized into the launcher's own OfflineScripts folder.
            AddRoot(roots, game);
            AddRoot(roots, Directory.GetParent(game)?.FullName);
            AddRoot(roots, Directory.GetParent(Directory.GetParent(game)?.FullName ?? game)?.FullName);
        }

        // Keep the launcher's bundled copy as a fallback.
        AddRoot(roots, launcherRoot);

        // First check the expected flat layouts. This keeps normal launches fast.
        foreach (var root in roots)
        {
            var match = FindCompleteSet(root, maxDepth: 0);
            if (match is not null) return match;
        }

        // Then inspect only a small, bounded set of nearby folders. Do not crawl the game
        // installation recursively (which can contain thousands of assets and DLLs).
        foreach (var root in roots)
        {
            var match = FindCompleteSet(root, maxDepth: 3);
            if (match is not null) return match;

            foreach (var folderName in SearchFolderNames)
            {
                var knownFolder = Path.Combine(root, folderName);
                if (!Directory.Exists(knownFolder)) continue;
                match = FindCompleteSet(knownFolder, maxDepth: 2);
                if (match is not null) return match;
            }
        }

        return null;
    }


    public bool IsInsideDirectory(string candidatePath, string parentDirectory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(parentDirectory), Path.GetFullPath(candidatePath));
        return !Path.IsPathRooted(relative) &&
               (relative == "." ||
                (relative != ".." &&
                 !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                 !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)));
    }

    public string CopyToLauncherFolder(string sourceDirectory, string launcherRoot)
    {
        var destination = Path.Combine(Path.GetFullPath(launcherRoot), "OfflineScripts");
        Directory.CreateDirectory(destination);

        foreach (var file in RequiredBootstrapFiles)
        {
            var source = Path.Combine(sourceDirectory, file);
            if (!File.Exists(source))
                throw new FileNotFoundException("Script offline obrigatório não encontrado.", source);

            var target = Path.Combine(destination, file);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target, overwrite: true);
        }

        return destination;
    }

    private static string? FindCompleteSet(string root, int maxDepth)
    {
        if (!Directory.Exists(root)) return null;
        var pending = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue((Path.GetFullPath(root), 0));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Dequeue();
            if (!visited.Add(current)) continue;

            if (RequiredBootstrapFiles.All(file => File.Exists(Path.Combine(current, file))))
                return current;

            if (depth >= maxDepth) continue;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(current))
                {
                    var name = Path.GetFileName(child);
                    if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("ufo_Data", StringComparison.OrdinalIgnoreCase))
                        continue;
                    pending.Enqueue((child, depth + 1));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return null;
    }

    private static void AddRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (!roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            roots.Add(fullPath);
    }
}
