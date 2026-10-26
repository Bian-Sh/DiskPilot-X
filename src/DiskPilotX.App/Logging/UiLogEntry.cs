using System.Windows.Media;
using Serilog.Events;

namespace DiskPilotX.App.Logging;

public sealed record UiLogEntry(DateTimeOffset Timestamp, LogEventLevel Level, string Message, string? Exception)
{
    public string TimeText => $"[{Timestamp:HH:mm:ss}]";

    public string LevelText => Level switch
    {
        LogEventLevel.Verbose => "[Verbose]",
        LogEventLevel.Debug => "[Debug]",
        LogEventLevel.Information => "[Info]",
        LogEventLevel.Warning => "[Warning]",
        LogEventLevel.Error => "[Error]",
        LogEventLevel.Fatal => "[Fatal]",
        _ => $"[{Level}]"
    };

    public Brush TimeBrush => Brushes.DeepSkyBlue;

    public Brush LevelBrush => Level switch
    {
        LogEventLevel.Verbose => new SolidColorBrush(Color.FromRgb(122, 162, 247)),
        LogEventLevel.Debug => new SolidColorBrush(Color.FromRgb(187, 154, 247)),
        LogEventLevel.Information => new SolidColorBrush(Color.FromRgb(158, 206, 106)),
        LogEventLevel.Warning => new SolidColorBrush(Color.FromRgb(224, 175, 104)),
        LogEventLevel.Error => new SolidColorBrush(Color.FromRgb(247, 118, 142)),
        LogEventLevel.Fatal => new SolidColorBrush(Color.FromRgb(255, 85, 110)),
        _ => Brushes.LightGray
    };

    public Brush MessageBrush => Level switch
    {
        LogEventLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 199, 119)),
        LogEventLevel.Error or LogEventLevel.Fatal => new SolidColorBrush(Color.FromRgb(255, 158, 171)),
        _ => new SolidColorBrush(Color.FromRgb(192, 202, 245))
    };

    public string DisplayMessage => string.IsNullOrWhiteSpace(Exception)
        ? Message
        : Message + Environment.NewLine + Exception;

    public string DisplayText => $"{TimeText} {LevelText} {DisplayMessage}";
}
