using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace RevivalRevolution.Services;

public sealed class RadminHostService
{
    public const string DefaultEndpoint = "http://26.226.57.19:8765";
    private const int ServerPort = 8765;
    private const int PeerPort = 8766;
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
            commands.AppendLine($"New-NetFirewallRule -DisplayName '{UdpRule}' -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol UDP -Program '{python}' -LocalPort {PeerPort} -RemoteAddress '26.0.0.0/8' | Out-Null");
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
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"(Get-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue | Measure-Object).Count");
        using var process = Process.Start(start);
        if (process is null) return false;
        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var output = (await outputTask).Trim();
        return process.ExitCode == 0 && int.TryParse(output, out var count) && count > 0;
    }

    public async Task EnsureHostServerAsync(string root, string pythonExe, string hostAddress,
        CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A integração automática Radmin exige Windows.");
        var module = Path.Combine(root, "ranked_server", "host_agent.py");
        if (!File.Exists(module))
            throw new FileNotFoundException("O agente do servidor não foi incluído na distribuição.", module);

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevivalRevolution", "RankedServer");
        Directory.CreateDirectory(dataRoot);
        var database = Path.Combine(dataRoot, "ranked.sqlite3");
        var pythonw = Path.Combine(Path.GetDirectoryName(pythonExe)!, "pythonw.exe");
        var executable = File.Exists(pythonw) ? pythonw : pythonExe;
        var arguments = new[]
        {
            "-m", "ranked_server.host_agent", "--host", hostAddress,
            "--port", ServerPort.ToString(), "--database", database, "--root", root
        };

        // HKCU Run is per-user and does not require elevation. Task Scheduler /Create
        // was failing with ERROR_ACCESS_DENIED for standard Windows accounts.
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
            if (runKey is not null)
            {
                var commandLine = QuoteWindowsArgument(executable) + " " +
                    string.Join(" ", arguments.Select(QuoteWindowsArgument));
                runKey.SetValue("RevivalRevolutionRankedHost", commandLine, RegistryValueKind.String);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The host can still run for this session; persistent logon startup is optional.
        }
        catch (System.Security.SecurityException)
        {
            // The host can still run for this session; persistent logon startup is optional.
        }

        if (!await IsHealthyAsync(hostAddress, token))
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = root
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var process = Process.Start(start)
                ?? throw new InvalidOperationException("Não foi possível iniciar o agente privado Radmin.");
            process.Dispose();
        }

        for (var attempt = 0; attempt < 35; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (await IsHealthyAsync(hostAddress, token)) return;
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        throw new InvalidOperationException("A API Ranked não respondeu em 35 segundos. Consulte os logs em " + dataRoot);
    }

    private static string QuoteWindowsArgument(string value) =>
        "\\"" + value.Replace("\\", "\\\\").Replace("\\"", "\\\\\\"") + "\\"";

    public async Task WaitForServerAsync(string hostAddress, CancellationToken token)
    {
        for (var attempt = 0; attempt < 35; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (await IsHealthyAsync(hostAddress, token)) return;
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        throw new HttpRequestException($"O servidor Radmin {hostAddress}:{ServerPort} não respondeu. Confirme se o anfitrião está ligado à rede VPN.");
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
