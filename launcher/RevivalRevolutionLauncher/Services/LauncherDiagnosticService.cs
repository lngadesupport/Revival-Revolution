namespace RevivalRevolution.Services;

public sealed record DiagnosticItem(string Name, bool Passed, string Detail);

public sealed class LauncherDiagnosticService
{
    private static readonly string[] Scripts =
    {
        "offline_launcher.py", "inject_offline.py", "inject_overlay.py",
        "peer_session.py", "autopunch_transport.py", "selection_sync.py", "live_fighter.py"
    };

    private static readonly string[] Libraries =
    {
        "MetalNetplayBridgeV32.dll", "MetalOfflineBridgeImGuiV3.dll",
        "MetalImGuiOverlay.dll", "SDL3.dll"
    };

    public IReadOnlyList<DiagnosticItem> Run(string root, string? gameFolder, string? pythonPath)
    {
        var results = new List<DiagnosticItem>();
        foreach (var name in Scripts) Check(results, name, Path.Combine(root, name));

        foreach (var name in Libraries)
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path) && gameFolder is not null) path = Path.Combine(gameFolder, name);
            Check(results, name, path);
        }

        Check(results, "Python portátil", pythonPath);
        Check(results, "ufo.exe", gameFolder is null ? null : Path.Combine(gameFolder, "ufo.exe"));
        var managed = gameFolder is null ? null : Path.Combine(gameFolder, "ufo_Data", "Managed");
        Check(results, "ufo_Data/Managed", managed, directory: true);
        CheckEmptyManagedLibraries(results, managed);
        return results;
    }

    private static void CheckEmptyManagedLibraries(List<DiagnosticItem> results, string? managed)
    {
        if (managed is null || !Directory.Exists(managed))
        {
            results.Add(new("DLLs vazias em Managed", false, "Pasta Managed não encontrada."));
            return;
        }

        try
        {
            var empty = Directory.EnumerateFiles(managed, "*.dll")
                .Where(path => new FileInfo(path).Length == 0)
                .Select(Path.GetFileName)
                .ToArray();
            results.Add(new("DLLs vazias em Managed", empty.Length == 0,
                empty.Length == 0 ? "Nenhuma DLL vazia encontrada." : string.Join(", ", empty)));
        }
        catch (Exception ex)
        {
            results.Add(new("DLLs vazias em Managed", false, "Não foi possível inspecionar a pasta: " + ex.Message));
        }
    }

    private static void Check(List<DiagnosticItem> list, string name, string? path, bool directory = false)
    {
        var exists = path is not null && (directory ? Directory.Exists(path) : File.Exists(path));
        list.Add(new(name, exists, exists ? path! : path ?? "Caminho não configurado"));
    }
}
