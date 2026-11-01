namespace RevivalRevolution.Services;

/// <summary>Locates the legacy game bootstrap independently from the WPF launcher and game folder.</summary>
public sealed class DistributionLocator
{
    private static readonly string[] RequiredBootstrapFiles =
    {
        "offline_launcher.py", "inject_offline.py", "inject_overlay.py"
    };

    public string? Find(string launcherRoot, string? gameFolder)
    {
        var candidates = new List<string> { Path.GetFullPath(launcherRoot) };
        if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            var game = Path.GetFullPath(gameFolder);
            var parent = Directory.GetParent(game)?.FullName;
            if (parent is not null) candidates.Add(parent);
            var grandParent = parent is null ? null : Directory.GetParent(parent)?.FullName;
            if (grandParent is not null) candidates.Add(grandParent);
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (RequiredBootstrapFiles.All(file => File.Exists(Path.Combine(candidate, file))))
                return candidate;
        }

        // Permit a partially staged distribution to be diagnosed at its most likely location.
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, "offline_launcher.py")));
    }
}
