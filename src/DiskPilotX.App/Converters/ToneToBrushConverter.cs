using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskPilotX.App.Converters;

/// <summary>把语义 tone（safe/warn/danger/accent）映射为主题画刷；未知 tone 回退文字主色。</summary>
public sealed class ToneToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var tone = value as string;
        var key = tone?.ToLowerInvariant() switch
        {
            "safe" or "success" or "green" => "VP.SuccessBrush",
            "warn" or "warning" or "caution" or "yellow" or "reviewfirst" => "VP.WarningBrush",
            "danger" or "error" or "red" => "VP.DangerBrush",
            "accent" or "blue" or "info" => "VP.AccentBrush",
            _ => "VP.TextPrimaryBrush",
        };
        return System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
