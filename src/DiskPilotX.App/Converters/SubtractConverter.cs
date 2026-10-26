using System.Globalization;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

/// <summary>
/// Subtracts a value from the input. Used to calculate width with margins.
/// </summary>
public class SubtractConverter : IValueConverter
{
    public double Subtrahend { get; set; } = 40; // Default margin

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double number)
        {
            var subtrahend = Subtrahend;
            if (parameter is string str && double.TryParse(str, out var parsed))
            {
                subtrahend = parsed;
            }
            return Math.Max(0, number - subtrahend);
        }

        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
