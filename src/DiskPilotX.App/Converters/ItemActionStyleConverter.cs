using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

/// <summary>
/// 条目行内动作按钮的样式选择：卸载这类破坏性动作取危险色实心按钮，
/// 其余动作取宿主通用按钮；颜色仍是 DynamicResource，跟随磨砂透明主题实时变化。
/// </summary>
public sealed class ItemActionStyleConverter : IValueConverter
{
    /// <summary>破坏性动作样式键。</summary>
    public const string DangerKey = "VP.DangerButton";
    /// <summary>普通动作样式键。</summary>
    public const string NormalKey = "VP.Button";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return null;

        var danger = value is true;
        var key = danger ? DangerKey : NormalKey;
        return resources[key] as Style ?? resources[NormalKey] as Style;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
