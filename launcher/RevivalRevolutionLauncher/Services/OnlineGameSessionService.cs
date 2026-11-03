using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace RevivalRevolution.Services;

public sealed class OnlineGameSessionService : IDisposable
{
    private const int PeerPort = 8766;
    private readonly string _distributionRoot;
    private readonly string _gameFolder;
    private readonly string _pythonExe;
    private Process? _peerProcess;
    private readonly object _logLock = new();

    public OnlineGameSessionService(string distributionRoot, string gameFolder, string pythonExe)
    {
        _distributionRoot = Path.GetFullPath(distributionRoot);
        _gameFolder = Path.GetFullPath(gameFolder);
        _pythonExe = Path.GetFullPath(pythonExe);
    }

    public async Task<string> StartHostAsync(CancellationToken cancellationToken = default)
    {
        var address = FindRadminAddress();
        await PrepareGameAsync(cancellationToken);
        ResetPeerFiles();
        StartPeer("host", address, null);

        var codePath = Path.Combine(_distributionRoot, "peer-code.txt");
        var statusPath = Path.Combine(_distributionRoot, "peer-status.json");
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_peerProcess is { HasExited: true })
                throw new InvalidOperationException("A sessão P2P encerrou antes de gerar o código: " + ReadPeerError(statusPath));
            if (File.Exists(codePath))
            {
                var code = (await File.ReadAllTextAsync(codePath, cancellationToken)).Trim();
                if (code.StartsWith("MR1-", StringComparison.Ordinal) && code.Length == 46)
                    return code;
            }
            var error = ReadPeerError(statusPath);
            if (!string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException("A sessão P2P não iniciou: " + error);
            await Task.Delay(250, cancellationToken);
        }

        Stop();
        throw new TimeoutException("A sessão P2P não gerou um código MR1 em 45 segundos. Consulte peer-status.json e peer-summary.txt.");
    }

    public async Task StartGuestAsync(string connectionCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionCode) ||
            !connectionCode.StartsWith("MR1-", StringComparison.Ordinal) ||
            connectionCode.Length != 46)
            throw new InvalidOperationException("O servidor não forneceu um código MR1 Radmin válido.");

        var address = FindRadminAddress();
        await PrepareGameAsync(cancellationToken);
        ResetPeerFiles();
        StartPeer("join", address, connectionCode);
    }

    public async Task WaitForPeerReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var statusPath = Path.Combine(_distributionRoot, "peer-status.json");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_peerProcess is { HasExited: true })
                throw new InvalidOperationException("A sessão P2P encerrou: " + ReadPeerError(statusPath));
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(statusPath, cancellationToken));
                var root = json.RootElement;
                if (root.TryGetProperty("state", out var state) && state.GetString() == "error")
                    throw new InvalidOperationException(root.TryGetProperty("message", out var message)
                        ? message.GetString() ?? "erro sem detalhes" : "erro sem detalhes");
                var connected = root.TryGetProperty("peer_connected", out var peer) && peer.GetBoolean();
                var localReady = root.TryGetProperty("local_vs_ready", out var local) && local.GetBoolean();
                var remoteReady = root.TryGetProperty("remote_vs_ready", out var remote) && remote.GetBoolean();
                if (connected && localReady && remoteReady) return;
            }
            catch (System.Text.Json.JsonException) { }
            catch (IOException) { }
            await Task.Delay(300, cancellationToken);
        }
        throw new TimeoutException("A sessão não concluiu o handshake UDP e a entrada VS no tempo limite. Confira peer-status.json e peer-summary.txt.");
    }

    private async Task PrepareGameAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_pythonExe))
            throw new FileNotFoundException("Python portátil não encontrado. Instale o runtime pelo launcher e tente novamente.", _pythonExe);
        if (!File.Exists(Path.Combine(_gameFolder, "ufo.exe")) ||
            !Directory.Exists(Path.Combine(_gameFolder, "ufo_Data", "Managed")))
            throw new InvalidOperationException("A pasta do jogo não contém ufo.exe e ufo_Data\\Managed.");

        var managed = Path.Combine(_gameFolder, "ufo_Data", "Managed");
        foreach (var assembly in new[] { "Assembly-CSharp.dll", "UnityEngine.CoreModule.dll" })
        {
            var assemblyPath = Path.Combine(managed, assembly);
            if (!File.Exists(assemblyPath) || new FileInfo(assemblyPath).Length == 0)
                throw new FileNotFoundException($"A instalação do jogo está incompleta: {assembly} ausente ou vazio em ufo_Data\\Managed.", assemblyPath);
        }

        var required = new[]
        {
            "offline_launcher.py", "inject_offline.py", "inject_overlay.py",
            "peer_session.py", "peer_native.py", "selection_sync.py",
            "live_fighter.py", "autopunch_transport.py",
            "MetalOfflineBridgeImGuiV3.dll", "MetalNetplayBridgeV8.dll", "MetalImGuiOverlay.dll"
        };
        var missing = required.Where(name =>
        {
            var path = Path.Combine(_distributionRoot, name);
            return !File.Exists(path) || (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && new FileInfo(path).Length == 0);
        }).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("Distribuição ONLINE incompleta: " + string.Join(", ", missing));

        var configPath = Path.Combine(_distributionRoot, "launcher-config.json");
        JsonObject launcherConfig;
        try
        {
            launcherConfig = File.Exists(configPath)
                ? JsonNode.Parse(await File.ReadAllTextAsync(configPath, cancellationToken)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (System.Text.Json.JsonException) { launcherConfig = new JsonObject(); }
        launcherConfig["game_folder"] = _gameFolder;
        await File.WriteAllTextAsync(configPath, launcherConfig.ToJsonString(), cancellationToken);

        var bootstrap = Path.Combine(_distributionRoot, "offline_launcher.py");
        var start = new ProcessStartInfo(_pythonExe)
        {
            WorkingDirectory = _distributionRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(bootstrap);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("O Windows não conseguiu iniciar o bootstrap do jogo.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("O bootstrap do jogo falhou. Confira launcher-error.txt. " +
                output.Trim().Split(Environment.NewLine).LastOrDefault());
        if (!File.Exists(Path.Combine(_distributionRoot, "netplay-pid.txt")))
            throw new InvalidOperationException("O jogo abriu, mas a bridge ONLINE não criou netplay-pid.txt. A sessão P2P não será iniciada.");
    }

    private void StartPeer(string mode, string address, string? code)
    {
        var start = new ProcessStartInfo(_pythonExe)
        {
            WorkingDirectory = _distributionRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.Combine(_distributionRoot, "peer_session.py"));
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(_distributionRoot);
        start.ArgumentList.Add("--mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("--bind");
        start.ArgumentList.Add(address);
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(PeerPort.ToString());
        start.ArgumentList.Add("--skip-native-preflight");
        start.ArgumentList.Add("--setup-timeout");
        start.ArgumentList.Add("600");
        if (mode == "host")
            start.ArgumentList.Add("--advertise");
        if (mode == "host")
            start.ArgumentList.Add(address);
        if (mode == "join")
        {
            start.ArgumentList.Add("--code");
            start.ArgumentList.Add(code!);
        }

        _peerProcess = Process.Start(start)
            ?? throw new InvalidOperationException("O Windows não conseguiu iniciar peer_session.py.");
    }

    private string FindRadminAddress()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var item in adapter.GetIPProperties().UnicastAddresses)
            {
                var address = item.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork &&
                    address.GetAddressBytes()[0] == 26)
                    return address.ToString();
            }
        }
        throw new InvalidOperationException("Radmin VPN não está conectada. Conecte-se à rede Radmin antes de iniciar a partida.");
    }

    private void ResetPeerFiles()
    {
        foreach (var name in new[] { "peer-code.txt", "peer-status.json", "peer-summary.txt", "peer-stop.json" })
        {
            var path = Path.Combine(_distributionRoot, name);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
        }
    }

    private static string ReadPeerError(string path)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.TryGetProperty("state", out var state) &&
                state.GetString() == "error" &&
                json.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? "erro sem detalhes";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException) { }
        return string.Empty;
    }

    public void Stop()
    {
        try
        {
            File.WriteAllText(Path.Combine(_distributionRoot, "peer-stop.json"), "{}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (_peerProcess is { HasExited: false })
        {
            try { _peerProcess.WaitForExit(1500); } catch (InvalidOperationException) { }
        }
        _peerProcess?.Dispose();
        _peerProcess = null;
    }

    public void Dispose() => Stop();
}
