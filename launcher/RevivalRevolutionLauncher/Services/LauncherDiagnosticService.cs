namespace RevivalRevolution.Services;

public sealed record DiagnosticItem(string Name, bool Passed, string Detail, bool Optional = false);

public sealed class LauncherDiagnosticService
{
    private static readonly string[] RequiredOfflineScripts =
    {
        "offline_launcher.py", "inject_offline.py", "inject_overlay.py",
    };

    private static readonly string[] OptionalOnlineScripts =
    {
        "peer_session.py", "autopunch_transport.py", "selection_sync.py", "live_fighter.py",
        "peer_native.py", "bridge_command.py", "fighter_input_test.py"
    };

    private static readonly string[] RequiredOfflineLibraries =
    {
        "MetalOfflineBridgeImGuiV3.dll"
    };

    private static readonly string[] OptionalOnlineLibraries =
    {
        "MetalNetplayBridgeV8.dll", "MetalImGuiOverlay.dll", "MetalGekko.dll", "SDL3.dll"
    };

    private readonly DistributionLocator _locator = new();

    public IReadOnlyList<DiagnosticItem> Run(string root, string? gameFolder, string? pythonPath)
    {
        var results = new List<DiagnosticItem>();
        var distribution = _locator.Find(root, gameFolder);
        var scriptRoot = distribution ?? root;
        foreach (var name in RequiredOfflineScripts)
            Check(results, "Essencial offline: " + name, Path.Combine(scriptRoot, name));

        foreach (var name in OptionalOnlineScripts)
        {
            var path = Path.Combine(scriptRoot, name);
            results.Add(new("Opcional online: " + name, true,
                File.Exists(path) ? path : "Ausente; recursos multiplayer que dependem deste componente ficam indisponíveis. O modo offline continua independente.", Optional: true));
        }

        foreach (var name in RequiredOfflineLibraries)
        {
            var candidates = new[] { distribution, gameFolder, root }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.Combine(path!, name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var found = candidates.FirstOrDefault(File.Exists);
            results.Add(new("Essencial offline: " + name, found is not null,
                found ?? "Não encontrado. Locais verificados: " + string.Join("; ", candidates)));
        }

        foreach (var name in OptionalOnlineLibraries)
        {
            var candidates = new[] { distribution, gameFolder, root }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.Combine(path!, name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var found = candidates.FirstOrDefault(File.Exists);
            results.Add(new("Opcional online: " + name, true,
                found ?? "Não instalado; não é necessário para iniciar no modo offline.", Optional: true));
        }

        Check(results, "Python portátil", pythonPath);
        Check(results, "ufo.exe", gameFolder is null ? null : Path.Combine(gameFolder, "ufo.exe"));
        var managed = gameFolder is null ? null : Path.Combine(gameFolder, "ufo_Data", "Managed");
        Check(results, "ufo_Data/Managed", managed, directory: true);
        CheckEmptyManagedLibraries(results, managed);
        if (distribution is null)
            results.Add(new("Raiz da distribuição", false,
                "Não foi possível localizar offline_launcher.py na pasta do launcher nem na pasta acima do jogo."));
        else
            results.Add(new("Raiz da distribuição", true, distribution));
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
        var detail = exists ? path! : path is null ? "Caminho não configurado" : "Não encontrado: " + path;
        list.Add(new(name, exists, detail));
    }
}
