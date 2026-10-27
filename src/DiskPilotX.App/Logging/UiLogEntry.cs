using System.Windows;
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

    public Brush TimeBrush => ThemeBrush("VP.TextTertiaryBrush", Colors.Gray);

    public Brush LevelBrush => Level switch
    {
        LogEventLevel.Verbose => ThemeBrush("VP.AccentTextBrush", Color.FromRgb(122, 162, 247)),
        LogEventLevel.Debug => ThemeBrush("VP.AccentHoverTextBrush", Color.FromRgb(187, 154, 247)),
        LogEventLevel.Information => ThemeBrush("VP.SuccessTextBrush", Color.FromRgb(158, 206, 106)),
        LogEventLevel.Warning => ThemeBrush("VP.WarningTextBrush", Color.FromRgb(224, 175, 104)),
        LogEventLevel.Error or LogEventLevel.Fatal => ThemeBrush("VP.DangerTextBrush", Color.FromRgb(247, 118, 142)),
        _ => ThemeBrush("VP.TextSecondaryBrush", Colors.Gray)
    };

    public Brush MessageBrush => Level switch
    {
        LogEventLevel.Warning => ThemeBrush("VP.WarningTextBrush", Color.FromRgb(255, 199, 119)),
        LogEventLevel.Error or LogEventLevel.Fatal => ThemeBrush("VP.DangerTextBrush", Color.FromRgb(255, 158, 171)),
        _ => ThemeBrush("VP.TextPrimaryBrush", Colors.Black)
    };

    private static Brush ThemeBrush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        var fallbackBrush = new SolidColorBrush(fallback);
        fallbackBrush.Freeze();
        return fallbackBrush;
    }

    public string DisplayMessage => string.IsNullOrWhiteSpace(Exception)
        ? Message
        : Message + Environment.NewLine + Exception;

    public string DisplayText => $"{TimeText} {LevelText} {DisplayMessage}";
}
