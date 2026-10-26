using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskPilotX.App.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#FF2562A7") to a WPF Color for use in bindings.
/// </summary>
[ValueConversion(typeof(string), typeof(Color))]
public class HexColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            return Services.ThemeManager.ParseColor(hex);
        }
        return Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return Services.ThemeManager.ToHex(color);
        }
        return "#FF000000";
    }
}
