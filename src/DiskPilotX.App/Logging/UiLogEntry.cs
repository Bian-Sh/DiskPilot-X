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

    public Brush TimeBrush => LogBrush("VP.LogTimeBrush", ThemeBrush("VP.TextTertiaryBrush", Colors.Gray));

    // 日志色系独立于界面强调色；下列基色仅作回退，明暗由日志专用主题画刷校正。
    private static readonly Brush VerboseLevelBrush = FreezeBrush(Color.FromRgb(122, 162, 247));
    private static readonly Brush DebugLevelBrush = FreezeBrush(Color.FromRgb(187, 154, 247));
    private static readonly Brush InfoLevelBrush = FreezeBrush(Color.FromRgb(158, 206, 106));
    private static readonly Brush WarningLevelBrush = FreezeBrush(Color.FromRgb(224, 175, 104));
    private static readonly Brush ErrorLevelBrush = FreezeBrush(Color.FromRgb(247, 118, 142));
    private static readonly Brush WarningMessageBrush = FreezeBrush(Color.FromRgb(255, 199, 119));
    private static readonly Brush ErrorMessageBrush = FreezeBrush(Color.FromRgb(255, 158, 171));

    public Brush LevelBrush => Level switch
    {
        LogEventLevel.Verbose => LogBrush("VP.LogVerboseBrush", VerboseLevelBrush),
        LogEventLevel.Debug => LogBrush("VP.LogDebugBrush", DebugLevelBrush),
        LogEventLevel.Information => LogBrush("VP.LogInfoBrush", InfoLevelBrush),
        LogEventLevel.Warning => LogBrush("VP.LogWarningBrush", WarningLevelBrush),
        LogEventLevel.Error or LogEventLevel.Fatal => LogBrush("VP.LogErrorBrush", ErrorLevelBrush),
        _ => ThemeBrush("VP.TextSecondaryBrush", Colors.Gray)
    };

    public Brush MessageBrush => Level switch
    {
        LogEventLevel.Warning => LogBrush("VP.LogWarningMessageBrush", WarningMessageBrush),
        LogEventLevel.Error or LogEventLevel.Fatal => LogBrush("VP.LogErrorMessageBrush", ErrorMessageBrush),
        _ => LogBrush("VP.LogMessageBrush", ThemeBrush("VP.TextPrimaryBrush", Colors.Black))
    };

    private static Brush LogBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static Brush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

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
