using System.Collections.ObjectModel;
using System.IO;
using Serilog;
using Serilog.Events;

namespace DiskPilotX.App.Logging;

public static class AppLog
{
    public static UiLogSink UiSink { get; } = new();

    public static ObservableCollection<UiLogEntry> Entries => UiSink.Entries;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskPilot-X",
        "logs");

    public static string FilePathPattern => Path.Combine(LogDirectory, "app-.log");

    public static void Configure()
    {
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.File(
                FilePathPattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(UiSink)
            .CreateLogger();
    }

    public static void CloseAndFlush() => Log.CloseAndFlush();
}
