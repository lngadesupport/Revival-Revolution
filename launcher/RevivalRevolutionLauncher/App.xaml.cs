using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;

namespace RevivalRevolution;

public partial class App : Application
{
    private static readonly string DiagnosticDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevivalRevolution",
        "Logs");

    private static readonly string DiagnosticPath = Path.Combine(
        DiagnosticDirectory,
        "startup-diagnostics.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Register handlers before WPF creates StartupUri/MainWindow so that
        // constructor and startup-event failures are captured as well.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        WriteDiagnostic("--------------------------------------------------");
        WriteDiagnostic("Launcher startup requested.");
        WriteDiagnostic($"Executable: {Environment.ProcessPath ?? "unknown"}");
        WriteDiagnostic($"Runtime: {Environment.Version}");
        WriteDiagnostic($"OS: {Environment.OSVersion}");
        WriteDiagnostic($"Administrator: {IsAdministrator()}");

        try
        {
            base.OnStartup(e);
            WriteDiagnostic("WPF startup sequence completed.");
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Fatal exception during WPF startup.", exception);
            MessageBox.Show(
                $"O launcher encontrou um erro ao iniciar e será encerrado.\n\n" +
                $"Detalhes registrados em:\n{DiagnosticPath}\n\n" +
                exception.Message,
                "Revival Revolution — erro de inicialização",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteDiagnostic($"Launcher exiting. ExitCode={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteDiagnostic("Unhandled WPF dispatcher exception.", e.Exception);

        try
        {
            MessageBox.Show(
                $"Ocorreu um erro inesperado e o launcher será encerrado para evitar " +
                $"continuar em um estado instável.\n\nO diagnóstico foi salvo em:\n{DiagnosticPath}\n\n" +
                e.Exception.Message,
                "Revival Revolution — erro inesperado",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Keep the original exception recorded even if Windows cannot show the dialog.
        }

        e.Handled = true;
        Shutdown(-1);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteDiagnostic($"Unhandled AppDomain exception. IsTerminating={e.IsTerminating}", exception);
        else
            WriteDiagnostic($"Unhandled AppDomain exception. IsTerminating={e.IsTerminating}; Object={e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        WriteDiagnostic("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Could not determine current Windows elevation.", exception);
            return false;
        }
    }

    internal static void WriteDiagnostic(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticDirectory);
            var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}";
            if (exception is not null)
                entry += Environment.NewLine + exception;
            File.AppendAllText(DiagnosticPath, entry + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never become a second startup failure.
        }
    }
}
