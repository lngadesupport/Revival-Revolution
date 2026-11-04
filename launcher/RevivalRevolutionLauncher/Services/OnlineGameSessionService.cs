using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace RevivalRevolution.Services;

public sealed class OnlineGameSessionService : IDisposable
{
    private const int PeerPort = 0; // Let Windows allocate a free UDP port for every session.
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

    public async Task<string> StartHostAsync(CancellationToken cancellationToken = default, string? preferredRadminAddress = null)
    {
        var address = FindHostRadminAddress(preferredRadminAddress);
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

        var address = FindLocalAddressForPeer(connectionCode);
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
                var connected = ReadBoolean(root, "peer_connected");
                var localReady = ReadBoolean(root, "local_vs_ready");
                var remoteReady = ReadBoolean(root, "remote_vs_ready");
                if (connected && localReady && remoteReady) return;
            }
            catch (System.Text.Json.JsonException) { }
            catch (IOException) { }
            catch (InvalidOperationException) { }
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
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
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

    private static bool IsRadminAddress(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] == 26;

    private string FindHostRadminAddress(string? preferredAddress)
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                .Where(item => IsRadminAddress(item.Address))
                .Select(item => new { Address = item.Address, Adapter = adapter }))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredAddress))
        {
            if (!IPAddress.TryParse(preferredAddress, out var preferred) || !IsRadminAddress(preferred))
                throw new InvalidOperationException("O endereço configurado do anfitrião não é um IPv4 Radmin válido.");
            var exact = candidates.FirstOrDefault(item => item.Address.Equals(preferred));
            if (exact is not null) return exact.Address.ToString();
            throw new InvalidOperationException(
                $"O IP Radmin configurado ({preferred}) não está ativo neste computador. " +
                "Conecte a Radmin VPN correta antes de criar a sala.");
        }

        if (candidates.Length == 1) return candidates[0].Address.ToString();
        var named = candidates.Where(item =>
            item.Adapter.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase) ||
            item.Adapter.Description.Contains("Radmin", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (named.Length == 1) return named[0].Address.ToString();

        var available = candidates.Select(item => item.Address.ToString()).Distinct().ToArray();
        if (available.Length == 0)
            throw new InvalidOperationException("Radmin VPN não está conectada. Abra a Radmin VPN e conecte-se à mesma rede do anfitrião.");
        throw new InvalidOperationException(
            "Há mais de uma interface VPN 26.x ativa e não foi possível escolher a correta: " +
            string.Join(", ", available) + ". Verifique a rede Radmin selecionada.");
    }

    private static string FindLocalAddressForPeer(string connectionCode)
    {
        var (remoteAddress, remotePort) = DecodeRadminConnectionCode(connectionCode);
        try
        {
            using var routeProbe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            routeProbe.Connect(new IPEndPoint(remoteAddress, remotePort));
            if (routeProbe.LocalEndPoint is not IPEndPoint local || !IsRadminAddress(local.Address))
                throw new InvalidOperationException(
                    "O Windows não encontrou uma rota Radmin até o anfitrião. Confirme se os dois PCs estão na mesma rede Radmin.");
            return local.Address.ToString();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                "Não foi possível selecionar a interface Radmin para alcançar o anfitrião. " +
                "Confirme se a VPN está conectada nos dois PCs.", ex);
        }
    }

    public static (IPAddress Address, int Port) DecodeRadminConnectionCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith("MR1-", StringComparison.Ordinal) || code.Length != 46)
            throw new InvalidOperationException("O convite P2P não contém um código MR1 Radmin válido.");

        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var decoded = new List<byte>(26);
        uint buffer = 0;
        var bits = 0;
        foreach (var character in code.AsSpan(4))
        {
            var value = alphabet.IndexOf(character);
            if (value < 0) throw new InvalidOperationException("O código de conexão contém caracteres inválidos.");
            buffer = (buffer << 5) | (uint)value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                decoded.Add((byte)(buffer >> bits));
                buffer &= bits == 0 ? 0u : (1u << bits) - 1;
            }
        }

        if (decoded.Count != 26 || (bits > 0 && buffer != 0))
            throw new InvalidOperationException("O código de conexão está incompleto ou malformado.");

        var bytes = decoded.ToArray();
        var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(22, 4));
        if (ComputeCrc32(bytes.AsSpan(0, 22)) != expectedCrc)
            throw new InvalidOperationException("A verificação de integridade do código de conexão falhou.");

        var address = new IPAddress(bytes.AsSpan(0, 4));
        var port = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
        if (!IsRadminAddress(address) || port == 0)
            throw new InvalidOperationException("O convite não aponta para um endpoint Radmin válido.");
        return (address, port);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private static bool ReadBoolean(System.Text.Json.JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        (value.ValueKind == System.Text.Json.JsonValueKind.True ||
         value.ValueKind == System.Text.Json.JsonValueKind.False) &&
        value.GetBoolean();

    private void ResetPeerFiles()
    {
        foreach (var name in new[] { "peer-code.txt", "peer-status.json", "peer-summary.txt", "peer-stop.json" })
        {
            var path = Path.Combine(_distributionRoot, name);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path))
                    throw new IOException("O arquivo de estado antigo continua presente.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Não foi possível limpar o estado antigo da sessão ({name}). Feche uma partida anterior e tente novamente.", ex);
            }
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
