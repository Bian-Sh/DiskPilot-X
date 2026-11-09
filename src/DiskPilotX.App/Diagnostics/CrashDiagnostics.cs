using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Text;
using DiskPilotX.App.Logging;
using Serilog;

namespace DiskPilotX.App.Diagnostics;

internal static class CrashDiagnostics
{
    private static int _isWritingCrashReport;

    public static string DiagnosticsDirectory => Path.Combine(Path.GetDirectoryName(AppLog.LogDirectory)!, "Diagnostics");

    public static string LogDirectory => Path.Combine(DiagnosticsDirectory, "logs");

    public static string DumpDirectory => Path.Combine(DiagnosticsDirectory, "dumps");

    public static void Register(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try { Directory.CreateDirectory(LogDirectory); Directory.CreateDirectory(DumpDirectory); }
        catch (Exception exception) { AppLog.WriteFailureDiagnostic(exception.ToString()); }
        Log("startup", "崩溃诊断已注册。");
    }

    public static void Log(string category, string message)
    {
        // 正常操作的排障埋点只记为调试日志，不在默认控制台误报崩溃。
        Serilog.Log.Debug("运行诊断 {Category}: {Message}", category, message);
    }

    public static void CaptureException(string source, Exception exception, bool terminating = false)
    {
        if (Interlocked.Exchange(ref _isWritingCrashReport, 1) != 0)
            return;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(DumpDirectory);

            var stamp = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var logPath = Path.Combine(LogDirectory, $"crash-{stamp}.log");
            var crashLog = BuildCrashLog(source, exception);
            var dumpPath = Path.Combine(DumpDirectory, $"crash-{stamp}.dmp");
            WriteDurably(logPath, writer => writer.Write(crashLog));
            var tsvPath = Path.Combine(LogDirectory, $"crash-{stamp}.tsv");
            WriteDurably(tsvPath, writer =>
            {
                LogTsvFormatter.WriteRow(writer, "时间", "来源", "进程", "异常类型", "消息", "异常堆栈", "应用目录", "系统", "运行时", "Dump路径", "Dump错误");
                LogTsvFormatter.WriteRow(writer, DateTimeOffset.Now.ToString("O"), source, Environment.ProcessId.ToString(),
                    exception.GetType().FullName, exception.Message, exception.ToString(), AppContext.BaseDirectory,
                    Environment.OSVersion.ToString(), RuntimeInformation.FrameworkDescription, dumpPath, "");
            });
            if (!TryWriteMiniDump(dumpPath, out var dumpError))
            {
                WriteDurably(logPath, writer => writer.WriteLine($"MiniDumpWriteDump 失败：{dumpError}"), append: true);
                WriteDurably(tsvPath, writer => LogTsvFormatter.WriteRow(writer, DateTimeOffset.Now.ToString("O"), "MiniDumpWriteDump", Environment.ProcessId.ToString(),
                    exception.GetType().FullName, exception.Message, exception.ToString(), AppContext.BaseDirectory,
                    Environment.OSVersion.ToString(), RuntimeInformation.FrameworkDescription, dumpPath, dumpError), append: true);
            }
            Serilog.Log.Fatal(exception, "Unhandled exception captured from {Source}; report={LogPath}; dump={DumpPath}", source, logPath, dumpPath);
        }
        catch (Exception reportError)
        {
            AppLog.WriteFailureDiagnostic($"崩溃报告写入失败：{reportError}\n原始异常：{exception}");
        }
        finally
        {
            Interlocked.Exchange(ref _isWritingCrashReport, 0);
            if (terminating)
            {
                try { Serilog.Log.CloseAndFlush(); }
                catch (Exception flushError) { AppLog.WriteFailureDiagnostic(flushError.ToString()); }
            }
        }
    }

    private static void WriteDurably(string path, Action<TextWriter> write, bool append = false)
    {
        using var stream = new FileStream(path, append ? FileMode.Append : FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        write(writer);
        writer.Flush();
        stream.Flush(true);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CaptureException("DispatcherUnhandledException", e.Exception, terminating: true);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CaptureException("AppDomain.UnhandledException", exception, terminating: e.IsTerminating);
        }
        else
        {
            CaptureException("AppDomain.UnhandledException", new InvalidOperationException(e.ExceptionObject?.ToString() ?? "未知异常对象"), terminating: e.IsTerminating);
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
