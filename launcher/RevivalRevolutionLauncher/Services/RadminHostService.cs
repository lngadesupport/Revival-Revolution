using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace RevivalRevolution.Services;

public sealed class RadminHostService
{
    public const string DefaultEndpoint = "http://26.226.57.19:8765";
    private const int ServerPort = 8765;
    private const string TcpRule = "Revival Revolution Ranked API - Radmin";
    private const string UdpRule = "Revival Revolution P2P - Radmin Python";
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static string? GetLocalRadminAddress(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !IPAddress.TryParse(uri.Host, out var target) || !IsRadminAddress(target))
            return null;

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && a.Equals(target))
            ?.ToString();
    }

    public static bool HasRadminAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork && IsRadminAddress(a.Address));

    private static bool IsRadminAddress(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] == 26;

    public async Task EnsureFirewallAsync(string pythonExe, string? hostAddress, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A integração automática Radmin exige Windows.");
        if (!File.Exists(pythonExe)) throw new FileNotFoundException("Runtime Python não encontrado.", pythonExe);

        var missingTcp = hostAddress is not null && !await FirewallRuleExistsAsync(TcpRule, token);
        var missingUdp = !await FirewallRuleExistsAsync(UdpRule, token);
        if (!missingTcp && !missingUdp) return;

        var python = pythonExe.Replace("'", "''");
        var commands = new StringBuilder();
        if (missingTcp)
        {
            commands.AppendLine($"New-NetFirewallRule -DisplayName '{TcpRule}' -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol TCP -LocalAddress '{hostAddress}' -LocalPort {ServerPort} -RemoteAddress '26.0.0.0/8' | Out-Null");
        }
        if (missingUdp)
        {
            commands.AppendLine($"New-NetFirewallRule -DisplayName '{UdpRule}' -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol UDP -Program '{python}' -RemoteAddress '26.0.0.0/8' | Out-Null");
        }

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'\n" + commands));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}"
        };
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível solicitar a configuração do Firewall do Windows.");
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("O Windows não confirmou a criação das regras de firewall Radmin.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("A autorização do Windows foi cancelada. Autorize a regra de rede uma única vez para habilitar as partidas pela Radmin.", ex);
        }

        if ((missingTcp && !await FirewallRuleExistsAsync(TcpRule, token)) ||
            (missingUdp && !await FirewallRuleExistsAsync(UdpRule, token)))
            throw new InvalidOperationException("As regras de firewall Radmin não foram encontradas após a configuração.");
    }

    private static async Task<bool> FirewallRuleExistsAsync(string name, CancellationToken token)
    {
        var start = new ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("advfirewall");
        start.ArgumentList.Add("firewall");
        start.ArgumentList.Add("show");
        start.ArgumentList.Add("rule");
        start.ArgumentList.Add("name=" + name);
        using var process = Process.Start(start);
        if (process is null) return false;
        await process.WaitForExitAsync(token);
        var output = await process.StandardOutput.ReadToEndAsync(token);
        return process.ExitCode == 0 && !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase);
    }

    public async Task EnsureHostServerAsync(string root, string pythonExe, string hostAddress,
        CancellationToken token)
    {
        if (await IsHealthyAsync(hostAddress, token)) return;
        var module = Path.Combine(root, "ranked_server", "__main__.py");
        if (!File.Exists(module))
            throw new FileNotFoundException("O pacote ranked_server não foi incluído na distribuição do launcher.", module);

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevivalRevolution", "RankedServer");
        Directory.CreateDirectory(dataRoot);
        var database = Path.Combine(dataRoot, "ranked.sqlite3");
        var pythonw = Path.Combine(Path.GetDirectoryName(pythonExe)!, "pythonw.exe");
        var executable = File.Exists(pythonw) ? pythonw : pythonExe;
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-m");
        start.ArgumentList.Add("ranked_server");
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add(hostAddress);
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(ServerPort.ToString());
        start.ArgumentList.Add("--database");
        start.ArgumentList.Add(database);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar a API Ranked em segundo plano.");
        process.Dispose(); // O servidor permanece ativo independentemente da janela do launcher.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (await IsHealthyAsync(hostAddress, token)) return;
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        throw new InvalidOperationException("A API Ranked não respondeu em 30 segundos. Consulte o log em " + Path.ChangeExtension(database, ".log"));
    }

    private static async Task<bool> IsHealthyAsync(string hostAddress, CancellationToken token)
    {
        try
        {
            using var response = await HealthClient.GetAsync($"http://{hostAddress}:{ServerPort}/health", token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            return false;
        }
    }
}
