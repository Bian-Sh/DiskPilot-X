using Serilog.Events;

namespace DiskPilotX.App.Logging;

[Flags]
public enum AppLogLevelFilter
{
    None = 0,
    Verbose = 1 << 0,
    Debug = 1 << 1,
    Information = 1 << 2,
    Warning = 1 << 3,
    Error = 1 << 4,
    Fatal = 1 << 5,
}

public static class AppLogLevelFilterExtensions
{
    public static bool Includes(this AppLogLevelFilter filter, LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => filter.HasFlag(AppLogLevelFilter.Verbose),
            LogEventLevel.Debug => filter.HasFlag(AppLogLevelFilter.Debug),
            LogEventLevel.Information => filter.HasFlag(AppLogLevelFilter.Information),
            LogEventLevel.Warning => filter.HasFlag(AppLogLevelFilter.Warning),
            LogEventLevel.Error => filter.HasFlag(AppLogLevelFilter.Error),
            LogEventLevel.Fatal => filter.HasFlag(AppLogLevelFilter.Fatal),
            _ => false
        };
    }
}
