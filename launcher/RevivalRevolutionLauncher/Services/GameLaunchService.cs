using System.Diagnostics;

namespace RevivalRevolution.Services;

public sealed class GameLaunchService
{
    public bool IsValidGameFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) &&
        File.Exists(Path.Combine(folder, "ufo.exe")) &&
        Directory.Exists(Path.Combine(folder, "ufo_Data", "Managed"));

    public void StartOffline(string pythonExe, string scriptPath, string gameFolder)
    {
        var workingDirectory = Path.GetFullPath(gameFolder);
        if (!File.Exists(pythonExe)) throw new FileNotFoundException("Python portátil não encontrado.", pythonExe);
        if (!File.Exists(scriptPath)) throw new FileNotFoundException("Script offline não encontrado.", scriptPath);
        if (!File.Exists(Path.Combine(workingDirectory, "ufo.exe")))
            throw new FileNotFoundException("ufo.exe não foi encontrado na pasta de trabalho do jogo.", Path.Combine(workingDirectory, "ufo.exe"));
        var logDirectory = Path.Combine(workingDirectory, "Launcher", "Logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"launcher-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
        var writeLock = new object();

        void WriteLog(string message)
        {
            try
            {
                lock (writeLock)
                    File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var start = new ProcessStartInfo(pythonExe)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(scriptPath);

        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) WriteLog(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) WriteLog("ERROR: " + e.Data); };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("O Windows não conseguiu iniciar o processo Python.");
            WriteLog($"Inicialização solicitada (PID {process.Id}).");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = Task.Run(() =>
            {
                try
                {
                    process.WaitForExit();
                    WriteLog($"Processo encerrado (código {process.ExitCode}).");
                }
                finally { process.Dispose(); }
            });
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}
