using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Serilog.Debugging;
using Serilog;
using Serilog.Events;

namespace DiskPilotX.App.Logging;

public static class AppLog
{
    public static UiLogSink UiSink { get; } = new();

    public static ObservableCollection<UiLogEntry> Entries => UiSink.Entries;

    public static string LogDirectory => Diagnostics.AcceptanceAutomationServer.IsEnabled()
        && Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS") is { Length: > 0 } acceptance
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(acceptance))!,"logs")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DiskPilot-X","logs");

    public static string FilePathPattern => Path.Combine(LogDirectory, $"app-{Environment.ProcessId}-.tsv");
    private static readonly object DiagnosticGate = new();
    private static long _lastDiagnostic;

    public static void Configure()
    {
        SelfLog.Enable(WriteFailureDiagnostic);
        try { Directory.CreateDirectory(LogDirectory); }
        catch (Exception exception) { WriteFailureDiagnostic(exception.ToString()); }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.File(
                new LogTsvFormatter(),
                FilePathPattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                encoding: new UTF8Encoding(true),
                hooks: new TsvLogFileHooks())
            .WriteTo.Sink(new UiLogForwarder())
            .CreateLogger();
    }

    public static void CloseAndFlush() { UiSink.Dispose(); Log.CloseAndFlush(); }

    // UI 缓存由应用退出负责释放，文件 logger 的独立重建不能提前销毁共享缓存。
    private sealed class UiLogForwarder : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => UiSink.Emit(logEvent);
    }

    internal static void WriteFailureDiagnostic(string message)
    {
        lock (DiagnosticGate)
        {
            var now = Environment.TickCount64;
            if (_lastDiagnostic != 0 && now - _lastDiagnostic < 1000) return;
            _lastDiagnostic = now;
            foreach (var directory in new[] { Path.Combine(LogDirectory, "diagnostics"), Path.Combine(Path.GetTempPath(), $"DiskPilot-X-{Environment.ProcessId}") })
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "logging-failure.tsv");
                    var empty = !File.Exists(path) || new FileInfo(path).Length == 0;
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(true));
                    if (empty) LogTsvFormatter.WriteRow(writer, "时间", "来源", "错误");
                    LogTsvFormatter.WriteRow(writer, DateTimeOffset.Now.ToString("O"), "日志落盘故障", message);
                    writer.Flush();
                    stream.Flush(true);
                    return;
                }
                catch { /* 独立故障记录不能回调故障日志链或再次抛出。 */ }
            }
        }
    }
}
