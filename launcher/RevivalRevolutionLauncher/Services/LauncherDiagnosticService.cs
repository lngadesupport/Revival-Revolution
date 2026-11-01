namespace RevivalRevolution.Services;

public sealed record DiagnosticItem(string Name, bool Passed, string Detail);
public sealed class LauncherDiagnosticService
{
    private static readonly string[] Scripts = { "offline_launcher.py","inject_offline.py","inject_overlay.py","peer_session.py","autopunch_transport.py","selection_sync.py","live_fighter.py" };
    private static readonly string[] Libraries = { "MetalNetplayBridgeV32.dll","MetalOfflineBridgeImGuiV3.dll","MetalImGuiOverlay.dll","SDL3.dll" };

    public IReadOnlyList<DiagnosticItem> Run(string root, string? gameFolder, string? pythonPath)
    {
        var results = new List<DiagnosticItem>();
        foreach (var name in Scripts) Check(results,name,Path.Combine(root,name));
        foreach (var name in Libraries)
        {
            var path = Path.Combine(root,name);
            if (!File.Exists(path) && gameFolder is not null) path = Path.Combine(gameFolder,name);
            Check(results,name,path);
        }
        Check(results,"Python portátil",pythonPath);
        Check(results,"ufo.exe",gameFolder is null ? null : Path.Combine(gameFolder,"ufo.exe"));
        Check(results,"ufo_Data/Managed",gameFolder is null ? null : Path.Combine(gameFolder,"ufo_Data","Managed"),true);
        var zero = gameFolder is null ? Array.Empty<string>() : Directory.Exists(Path.Combine(gameFolder,"ufo_Data","Managed"))
            ? Directory.GetFiles(Path.Combine(gameFolder,"ufo_Data","Managed"),"*.dll").Where(f => new FileInfo(f).Length == 0).Select(Path.GetFileName).ToArray()
            : Array.Empty<string>();
        results.Add(new("DLLs vazias em Managed",zero.Length == 0,string.Join(", ",zero)));
        return results;
    }
    private static void Check(List<DiagnosticItem> list,string name,string? path,bool directory=false)
    {
        var ok = path is not null && (directory ? Directory.Exists(path) : File.Exists(path));
        list.Add(new(name,ok,path ?? "Caminho não configurado"));
    }
}
