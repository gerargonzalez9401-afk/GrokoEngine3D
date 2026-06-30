using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrokoEngine.ImGuiEditor;

internal static class EditorDiagnostics
{
    private static readonly object Gate = new();
    private static string? currentLogPath;

    public static string LogsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GrokoEngine",
            "Logs");

    public static string CurrentLogPath
    {
        get
        {
            EnsureStarted();
            return currentLogPath!;
        }
    }

    public static void InstallGlobalExceptionHandlers()
    {
        EnsureStarted();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            WriteCrash(exception, "AppDomain.CurrentDomain.UnhandledException", e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash(e.Exception, "TaskScheduler.UnobservedTaskException", false);
            e.SetObserved();
        };
    }

    public static void Log(string message, string severity = "Info")
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        EnsureStarted();
        WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{severity}] {message}");
    }

    public static void LogException(Exception ex, string context, bool terminating = false)
    {
        WriteCrash(ex, context, terminating);
    }

    private static void EnsureStarted()
    {
        if (currentLogPath != null)
            return;

        lock (Gate)
        {
            if (currentLogPath != null)
                return;

            Directory.CreateDirectory(LogsDirectory);
            currentLogPath = Path.Combine(LogsDirectory, $"editor-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
            WriteLine("============================================================");
            WriteLine("GrokoEngine Editor diagnostics started");
            WriteLine($"ProcessId: {Environment.ProcessId}");
            WriteLine($"BaseDirectory: {AppContext.BaseDirectory}");
            WriteLine($".NET: {Environment.Version}");
            WriteLine($"OS: {Environment.OSVersion}");
            WriteLine("============================================================");
        }
    }

    private static void WriteCrash(Exception? ex, string context, bool terminating)
    {
        EnsureStarted();

        var builder = new StringBuilder();
        builder.AppendLine("============================================================");
        builder.AppendLine($"CRASH REPORT: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"Context: {context}");
        builder.AppendLine($"Terminating: {terminating}");
        builder.AppendLine($"Thread: {Thread.CurrentThread.ManagedThreadId}");
        builder.AppendLine(ex?.ToString() ?? "No Exception object was provided.");
        builder.AppendLine("============================================================");

        WriteLine(builder.ToString().TrimEnd());
    }

    private static void WriteLine(string text)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(CurrentLogPathUnsafe(), text + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never crash the editor.
        }
    }

    private static string CurrentLogPathUnsafe()
    {
        if (currentLogPath == null)
        {
            Directory.CreateDirectory(LogsDirectory);
            currentLogPath = Path.Combine(LogsDirectory, $"editor-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        }

        return currentLogPath;
    }
}
