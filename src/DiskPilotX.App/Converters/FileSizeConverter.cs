using System.Globalization;
using System.Windows.Data;

namespace DiskPilotX.App.Converters;

/// <summary>
/// Converts a long byte value to a human-readable file size string (e.g., "1.5 MB").
/// </summary>
public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return FormatSize(bytes);
        }

        return "0 bytes";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("FileSizeConverter does not support ConvertBack.");
    }

    /// <summary>
    /// Formats a byte count as a human-readable string.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>A formatted string such as "1.5 MB" or "512 bytes".</returns>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} bytes";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        if (bytes < 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }

        if (bytes < 1024L * 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }
}
