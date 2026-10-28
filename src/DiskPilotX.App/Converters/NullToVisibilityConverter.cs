using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 空串与纯空白同样视为“没有内容”，避免出现空的标签/说明块占位。
        return value is null or string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
