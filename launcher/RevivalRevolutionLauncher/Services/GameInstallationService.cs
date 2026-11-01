using System.Diagnostics;

namespace RevivalRevolution.Services;

public sealed class GameInstallationService
{
    public bool IsValidGameFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        return File.Exists(Path.Combine(folder, "ufo.exe")) &&
               Directory.Exists(Path.Combine(folder, "ufo_Data", "Managed"));
    }

    public Task LaunchOfflineAsync(string pythonExe, string scriptPath, string workingDirectory)
    {
        var logs = Path.Combine(workingDirectory, "Launcher", "Logs");
        Directory.CreateDirectory(logs);
        var logPath = Path.Combine(logs, $"launcher-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var start = new ProcessStartInfo
        {
            FileName = pythonExe,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(scriptPath);

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {e.Data}{Environment.NewLine}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] ERROR: {e.Data}{Environment.NewLine}");
        };

        if (!process.Start())
            throw new InvalidOperationException("O Windows não conseguiu iniciar o processo Python.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Inicialização solicitada.{Environment.NewLine}");
        return Task.CompletedTask;
    }
}
