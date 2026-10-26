using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DiskPilotX.Core.Enums;

namespace DiskPilotX.App.Converters;

/// <summary>
/// Converts SafetyRating enum values to WPF Brush objects for visual representation.
/// </summary>
public class SafetyRatingToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SafeBrush = CreateFrozenBrush("#4CAF50");
    private static readonly SolidColorBrush ReviewFirstBrush = CreateFrozenBrush("#FF9800");
    private static readonly SolidColorBrush CautionBrush = CreateFrozenBrush("#F44336");
    private static readonly SolidColorBrush DefaultBrush = CreateFrozenBrush("#9E9E9E");

    /// <summary>
    /// Creates a frozen SolidColorBrush from a hex color string.
    /// </summary>
    private static SolidColorBrush CreateFrozenBrush(string hexColor)
    {
        var color = (Color)ColorConverter.ConvertFromString(hexColor);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Converts a SafetyRating enum value to a corresponding SolidColorBrush.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SafetyRating rating)
        {
            return DefaultBrush;
        }

        return rating switch
        {
            SafetyRating.Safe => SafeBrush,
            SafetyRating.ReviewFirst => ReviewFirstBrush,
            SafetyRating.Caution => CautionBrush,
            _ => DefaultBrush
        };
    }

    /// <summary>
    /// Conversion back from Brush to SafetyRating is not supported.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Converting from Brush to SafetyRating is not supported.");
    }
}
