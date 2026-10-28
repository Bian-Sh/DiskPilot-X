using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

/// <summary>
/// 动态窗口底部动作按钮统一取宿主通用按钮样式：主动作（回传选中项）走强调色样式，
/// 其余走普通按钮样式。样式只在应用级资源里取一次，内部颜色仍是 DynamicResource，
/// 因此会跟随磨砂透明主题实时变化。
/// </summary>
public sealed class ActionButtonStyleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return null;

        var key = value is true ? PrimaryKey : NormalKey;
        return resources[key] as Style ?? resources[NormalKey] as Style;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    /// <summary>主动作样式键（强调色）。</summary>
    public const string PrimaryKey = "VP.AccentButton";
    /// <summary>普通动作样式键。</summary>
    public const string NormalKey = "VP.Button";
}
