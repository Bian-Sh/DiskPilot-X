using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace DiskPilotX.App.Diagnostics;

internal static class CrashDiagnostics
{
    private static int _isWritingCrashReport;

    public static string DiagnosticsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskPilot-X",
        "Diagnostics");

    public static string LogDirectory { get; } = Path.Combine(DiagnosticsDirectory, "logs");

    public static string DumpDirectory { get; } = Path.Combine(DiagnosticsDirectory, "dumps");

    public static void Register(Application app)
    {
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(DumpDirectory);

        Log("startup", "Crash diagnostics registered.");

        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void Log(string category, string message)
    {
        Serilog.Log.Information("Crash diagnostics {Category}: {Message}", category, message);
    }

    public static void CaptureException(string source, Exception exception)
    {
        if (Interlocked.Exchange(ref _isWritingCrashReport, 1) != 0)
            return;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(DumpDirectory);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var logPath = Path.Combine(LogDirectory, $"crash-{stamp}.log");
            var crashLog = BuildCrashLog(source, exception);
            var dumpPath = Path.Combine(DumpDirectory, $"crash-{stamp}.dmp");
            if (!TryWriteMiniDump(dumpPath, out var dumpError))
            {
                crashLog += $"{Environment.NewLine}MiniDumpWriteDump failed: {dumpError}{Environment.NewLine}";
            }

            File.WriteAllText(logPath, crashLog);
            Serilog.Log.Fatal(exception, "Unhandled exception captured from {Source}; report={LogPath}; dump={DumpPath}", source, logPath, dumpPath);
        }
        catch
        {
            // Last-chance diagnostics must not throw.
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CaptureException("DispatcherUnhandledException", e.Exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CaptureException("AppDomain.UnhandledException", exception);
        }
        else
        {
            CaptureException("AppDomain.UnhandledException", new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown exception object"));
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CaptureException("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private static string BuildCrashLog(string source, Exception exception)
    {
        var process = Process.GetCurrentProcess();
        return $"""
Source: {source}
Time: {DateTime.Now:O}
Process: {process.ProcessName} ({process.Id})
AppBase: {AppContext.BaseDirectory}
OS: {Environment.OSVersion}
Runtime: {RuntimeInformation.FrameworkDescription}
Exception:
{exception}
""";
    }

    private static bool TryWriteMiniDump(string dumpPath, out string? error)
    {
        error = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            using var dumpFile = File.Create(dumpPath);
            var result = MiniDumpWriteDump(
                process.Handle,
                process.Id,
                dumpFile.SafeFileHandle.DangerousGetHandle(),
                MiniDumpType.MiniDumpWithDataSegs
                    | MiniDumpType.MiniDumpWithHandleData
                    | MiniDumpType.MiniDumpWithThreadInfo
                    | MiniDumpType.MiniDumpWithUnloadedModules,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (result)
                return true;

            error = $"Win32Error={Marshal.GetLastWin32Error()}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            return false;
        }
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        int processId,
        IntPtr fileHandle,
        MiniDumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [Flags]
    private enum MiniDumpType
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithThreadInfo = 0x00001000
    }
}
