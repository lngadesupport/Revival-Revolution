using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace RevivalRevolution.Services;

public sealed class PythonRuntimeService
{
    public const string RuntimeVersion = "3.13.17";
    public const string RuntimeUrl = "https://www.python.org/ftp/python/3.13.17/python-3.13.17-embed-amd64.zip";
    private const string RuntimeSha256 = "9e35319bffedab8b36ad44d8f377cb4f1733bcec257a0b6d04d94f01486ddb38";
    private readonly DownloadService _downloads = new();
    private readonly ArchiveExtractionService _archives = new();

    public string? FindRuntime(string root) => new[]
    {
        Path.Combine(root, "Launcher", "Runtime", "Python", "python.exe"),
        Path.Combine(root, "Runtime", "Python", "python.exe"),
        Path.Combine(root, "python.exe")
    }.FirstOrDefault(File.Exists);

    public async Task<string> InstallAsync(string root, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        var zip = Path.Combine(root, "Launcher", "Downloads", $"python-{RuntimeVersion}-embed-amd64.zip");
        var target = Path.Combine(root, "Launcher", "Runtime", "Python");
        var staging = target + ".staging";
        var backup = target + ".backup";

        await _downloads.DownloadAsync(RuntimeUrl, zip, progress, token);
        await using (var stream = File.OpenRead(zip))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
            if (!hash.Equals(RuntimeSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O hash SHA-256 do runtime Python não confere com o arquivo oficial.");
        }

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            await _archives.ExtractZipSafelyAsync(zip, staging, null, token);

            var pth = Path.Combine(staging, "python313._pth");
            var exe = Path.Combine(staging, "python.exe");
            if (!File.Exists(pth) || !File.Exists(exe))
                throw new InvalidDataException("O pacote Python está incompleto (python.exe/python313._pth ausente).");

            var lines = (await File.ReadAllLinesAsync(pth, token))
                .Where(line => line.Trim() is not "." and not "import site")
                .Append(".").Append("..\\..\\..").Append("import site");
            await File.WriteAllLinesAsync(pth, lines, token);
            await VerifyRuntimeAsync(exe, token);

            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(staging, target); }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(target)) Directory.Move(backup, target);
                throw;
            }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);

            var installed = Path.Combine(target, "python.exe");
            await File.WriteAllTextAsync(Path.Combine(root, "python-runtime.txt"), installed, token);
            var game = Path.Combine(root, "Metal Revolution");
            if (Directory.Exists(game))
                await File.WriteAllTextAsync(Path.Combine(game, "python-runtime.txt"), installed, token);
            return installed;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static async Task VerifyRuntimeAsync(string executable, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start()) throw new InvalidOperationException("Não foi possível iniciar o Python portátil.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch { } throw; }
        var version = (await output) + (await error);
        if (process.ExitCode != 0 || !version.Contains($"Python {RuntimeVersion}", StringComparison.Ordinal))
            throw new InvalidDataException("O Python portátil extraído não passou no teste de inicialização: " + version.Trim());
    }
}
