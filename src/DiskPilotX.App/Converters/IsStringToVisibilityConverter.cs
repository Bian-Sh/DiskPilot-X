using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

public class IsStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isString = value is string;
        if (parameter is string flag && flag.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            isString = !isString;
        }

        return isString ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
