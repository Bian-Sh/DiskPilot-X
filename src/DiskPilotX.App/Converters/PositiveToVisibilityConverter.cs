using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

/// <summary>数值大于 0 才显示；用于入口卡上「有数据才亮」的统计标签。</summary>
public sealed class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var number = value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            double doubleValue => (long)doubleValue,
            _ => 0L
        };
        return number > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
