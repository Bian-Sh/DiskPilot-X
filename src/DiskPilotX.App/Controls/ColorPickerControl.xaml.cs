using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiskPilotX.App.Controls;

public partial class ColorPickerControl : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorPickerControl),
            new FrameworkPropertyMetadata(Color.FromArgb(255, 37, 98, 167),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private double _hue;
    private double _saturation = 1.0;
    private double _brightness = 1.0;
    private bool _isDraggingSV;
    private bool _isDraggingHue;
    private bool _suppressUpdate;

    public ColorPickerControl()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncFromColor(SelectedColor);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPickerControl picker && !picker._suppressUpdate)
        {
            picker.SyncFromColor((Color)e.NewValue);
        }
    }

    private void SyncFromColor(Color c)
    {
        ColorToHSV(c, out var h, out var s, out var v);
        _hue = h;
        _saturation = s;
        _brightness = v;
        UpdateUI();
    }

    private void UpdateUI()
    {
        var hueColor = HSVToColor(_hue, 1.0, 1.0);
        HueColorStop.Color = hueColor;

        var svWidth = SVCanvas.ActualWidth > 0 ? SVCanvas.ActualWidth : 260;
        var svHeight = SVCanvas.ActualHeight > 0 ? SVCanvas.ActualHeight : 140;

        Canvas.SetLeft(SVSelector, _saturation * svWidth - 7);
        Canvas.SetTop(SVSelector, (1.0 - _brightness) * svHeight - 7);

        var hueWidth = HueCanvas.ActualWidth > 0 ? HueCanvas.ActualWidth : 260;
        Canvas.SetLeft(HueSelector, (_hue / 360.0) * hueWidth - 2);

        var finalColor = HSVToColor(_hue, _saturation, _brightness);
        PreviewBrush.Color = finalColor;
        HexTextBox.Text = ColorToHex(finalColor);
    }

    private void SetColorFromSV(double x, double y)
    {
        var svWidth = SVCanvas.ActualWidth > 0 ? SVCanvas.ActualWidth : 260;
        var svHeight = SVCanvas.ActualHeight > 0 ? SVCanvas.ActualHeight : 140;

        _saturation = Math.Clamp(x / svWidth, 0, 1);
        _brightness = Math.Clamp(1.0 - (y / svHeight), 0, 1);

        var color = HSVToColor(_hue, _saturation, _brightness);
        _suppressUpdate = true;
        SelectedColor = color;
        _suppressUpdate = false;
        UpdateUI();
    }

    private void SetColorFromHue(double x)
    {
        var hueWidth = HueCanvas.ActualWidth > 0 ? HueCanvas.ActualWidth : 260;
        _hue = Math.Clamp((x / hueWidth) * 360.0, 0, 360);

        var color = HSVToColor(_hue, _saturation, _brightness);
        _suppressUpdate = true;
        SelectedColor = color;
        _suppressUpdate = false;
        UpdateUI();
    }

    // SV Canvas events
    private void SVCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSV = true;
        SVCanvas.CaptureMouse();
        var pos = e.GetPosition(SVCanvas);
        SetColorFromSV(pos.X, pos.Y);
    }

    private void SVCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSV) return;
        var pos = e.GetPosition(SVCanvas);
        SetColorFromSV(pos.X, pos.Y);
    }

    private void SVCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSV = false;
        SVCanvas.ReleaseMouseCapture();
    }

    // Hue Canvas events
    private void HueCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        HueCanvas.CaptureMouse();
        var pos = e.GetPosition(HueCanvas);
        SetColorFromHue(pos.X);
    }

    private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingHue) return;
        var pos = e.GetPosition(HueCanvas);
        SetColorFromHue(pos.X);
    }

    private void HueCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = false;
        HueCanvas.ReleaseMouseCapture();
    }

    // Hex input
    private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHexInput();
            e.Handled = true;
        }
    }

    private void HexTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyHexInput();
    }

    private void ApplyHexInput()
    {
        try
        {
            var hex = HexTextBox.Text.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            var color = (Color)ColorConverter.ConvertFromString(hex);
            _suppressUpdate = true;
            SelectedColor = color;
            _suppressUpdate = false;
            SyncFromColor(color);
        }
        catch
        {
            // Invalid hex, revert to current color
            UpdateUI();
        }
    }

    // Color conversion helpers
    private static void ColorToHSV(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;

        s = max > 0 ? delta / max : 0;
        v = max;
    }

    private static Color HSVToColor(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromArgb(255,
            (byte)Math.Clamp((r + m) * 255, 0, 255),
            (byte)Math.Clamp((g + m) * 255, 0, 255),
            (byte)Math.Clamp((b + m) * 255, 0, 255));
    }

    private static string ColorToHex(Color c)
    {
        return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
