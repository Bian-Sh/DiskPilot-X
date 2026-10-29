using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

/// <summary>
/// 把「当前值 / 最大值」折算成实际长度，供条形图、进度条等按可用宽度缩放。
/// 用法：&lt;MultiBinding Converter="{StaticResource RatioLen}"&gt;
/// 依次传入 value 与 max，Factor 指定满格长度。
/// </summary>
public sealed class RatioToLengthConverter : IMultiValueConverter
{
    /// <summary>满格（ratio = 1）时对应的长度；可为固定值，也可绑定容器宽度。</summary>
    public double Factor { get; set; } = 120;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var value = ToDouble(values.ElementAtOrDefault(0));
        var max = ToDouble(values.ElementAtOrDefault(1));
        var width = values.Length > 2 ? ToDouble(values[2]) : Factor;
        if (!double.IsFinite(value) || value <= 0 || !double.IsFinite(max) || max <= 0 || !double.IsFinite(width) || width <= 0) return 0d;
        var ratio = value / max;
        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0) ratio = 0;
        if (ratio > 1) ratio = 1;
        return ratio * width;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double ToDouble(object? input) => input switch
    {
        null => 0d,
        IConvertible convertible => System.Convert.ToDouble(convertible, CultureInfo.InvariantCulture),
        _ => 0d
    };
}
