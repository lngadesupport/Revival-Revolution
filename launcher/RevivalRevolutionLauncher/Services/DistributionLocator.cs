namespace RevivalRevolution.Services;

/// <summary>
/// Searches the selected game installation recursively, finds each bootstrap file independently,
/// and stages the complete set in the game's own OfflineScripts folder.
/// </summary>
public sealed class DistributionLocator
{
    private static readonly string[] RequiredBootstrapFiles =
    {
        "offline_launcher.py", "inject_offline.py", "inject_overlay.py"
    };

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "__pycache__", "Temp", "Cache",
        "Logs", "Downloads", "Runtime", "Library"
    };

    private const int MaxDirectoriesToInspect = 50000;
    private const long MaxScriptBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Locates every required script under the game folder (and launcher folder when distinct).
    /// Files may be in different subfolders. Found files are copied individually to
    /// <game folder>\OfflineScripts. Returns that canonical folder only when all are present.
    /// </summary>
    public string? Find(string launcherRoot, string? gameFolder)
    {
        var destinationRoot = Path.GetFullPath(
            !string.IsNullOrWhiteSpace(gameFolder) ? gameFolder : launcherRoot);
        var destination = Path.Combine(destinationRoot, "OfflineScripts");
        Directory.CreateDirectory(destination);

        var roots = new List<string>();
        AddRoot(roots, destinationRoot);
        AddRoot(roots, launcherRoot);

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in RequiredBootstrapFiles)
        {
            var existing = Path.Combine(destination, name);
            if (IsPlausibleScript(existing))
                found[name] = existing;
        }

        foreach (var root in roots)
        {
            foreach (var file in SearchFiles(root))
            {
                var name = Path.GetFileName(file);
                if (!RequiredBootstrapFiles.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                    found.ContainsKey(name) || !IsPlausibleScript(file))
                    continue;

                found[name] = file;
                if (found.Count == RequiredBootstrapFiles.Length) break;
            }
            if (found.Count == RequiredBootstrapFiles.Length) break;
        }

        foreach (var name in RequiredBootstrapFiles)
        {
            if (!found.TryGetValue(name, out var source)) continue;
            var target = Path.Combine(destination, name);
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(source, target, overwrite: true);
        }

        return RequiredBootstrapFiles.All(name => IsPlausibleScript(Path.Combine(destination, name)))
            ? destination
            : null;
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
            if (!IsPlausibleScript(source))
                throw new FileNotFoundException("Script offline obrigatório não encontrado ou inválido.", source);

            var target = Path.Combine(destination, file);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target, overwrite: true);
        }

        return destination;
    }

    private static IEnumerable<string> SearchFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;

        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(Path.GetFullPath(root));
        var inspected = 0;

        while (pending.Count > 0 && inspected++ < MaxDirectoriesToInspect)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current).ToArray(); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var file in files)
                yield return file;

            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(current).ToArray(); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var child in directories)
            {
                var name = Path.GetFileName(child);
                if (IgnoredDirectoryNames.Contains(name)) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                pending.Enqueue(child);
            }
        }
    }

    private static bool IsPlausibleScript(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > MaxScriptBytes) return false;
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(512, (int)info.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);
            var prefix = System.Text.Encoding.UTF8.GetString(buffer, 0, read)
                .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            return !prefix.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) &&
                   !prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase) &&
                   !prefix.StartsWith("<head", StringComparison.OrdinalIgnoreCase) &&
                   !prefix.StartsWith("<form", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void AddRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (!roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            roots.Add(fullPath);
    }
}
