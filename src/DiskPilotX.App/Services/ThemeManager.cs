using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Services;

/// <summary>
/// 将主题配色、统一磨砂材质和文字对比度实时应用到 WPF 资源。
/// </summary>
public class ThemeManager
{
    private readonly ISettingsService _settingsService;
    private ThemeProfile _currentProfile;
    private Window? _mainWindow;
    private bool _glassBackdropActive;
    private const double MinimumGlassTintOpacity = 0.08;

    public ThemeProfile CurrentProfile => _currentProfile;

    public event Action? ThemeChanged;

    public ThemeManager(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _currentProfile = ThemeProfile.VisionProDark;
    }

    public void Initialize()
    {
        var settings = _settingsService.LoadSettings();
        if (settings.ThemeProfile != null)
        {
            _currentProfile = settings.ThemeProfile;
        }
    }

    public void RegisterMainWindow(Window window)
    {
        _mainWindow = window;
        _glassBackdropActive = false;
        Refresh();
    }

    public void ApplyTheme(ThemeProfile profile)
    {
        _currentProfile = profile;
        Refresh();
        Persist();
        ThemeChanged?.Invoke();
    }

    public void Refresh()
    {
        ApplyGlassBackdrop();
        ApplyToResources();
    }

    public void Persist()
    {
        var settings = _settingsService.LoadSettings();
        settings.ThemeProfile = _currentProfile;
        _settingsService.SaveSettings(settings);
    }

    public static string GetContrastTextColor(string backgroundHex)
    {
        var bg = ParseColor(backgroundHex);
        double luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return luminance > 0.5 ? "#FF1C1C1E" : "#FFFFFFFF";
    }

    private void ApplyToResources()
    {
        var app = Application.Current;
        if (app == null) return;

        var textPrimary = _currentProfile.AutoTextContrast
            ? GetContrastTextColor(_currentProfile.BackgroundColor)
            : _currentProfile.TextPrimaryColor;

        var textSecondary = _currentProfile.AutoTextContrast
            ? AdjustAlpha(textPrimary, 0.78)
            : _currentProfile.TextSecondaryColor;

        var textTertiary = _currentProfile.AutoTextContrast
            ? AdjustAlpha(textPrimary, 0.60)
            : _currentProfile.TextTertiaryColor;

        var profile = _currentProfile;

        // 显式颜色源让共享画刷始终保持绑定，已有文字无需重建也能收到更新。
        void SetBrush(string key, Color color)
        {
            var brush = app.Resources[key] as SolidColorBrush;
            if (brush is not null && !brush.IsFrozen
                && BindingOperations.GetBinding(brush, SolidColorBrush.ColorProperty)?.Source is ThemeColor source)
            {
                source.Value = color;
                return;
            }

            if (brush is null || brush.IsFrozen) brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty,
                new Binding(nameof(ThemeColor.Value)) { Source = new ThemeColor(color), Mode = BindingMode.OneWay });
            app.Resources[key] = brush;
        }

        void SetColor(string key, Color color)
        {
            app.Resources[key] = color;
            if (key != "VP.Background")
                SetBrush(key == "VP.BackgroundTint" ? "VP.BackgroundBrush" : key + "Brush", color);
        }

        var bg = ParseColor(profile.BackgroundColor);
        var bgAlt = ParseColor(profile.BackgroundAltColor);
        var accent = ParseColor(profile.AccentColor);
        var accentHover = ParseColor(profile.AccentHoverColor);
        var tPrimary = ParseColor(textPrimary);
        var tSecondary = ParseColor(textSecondary);
        var tTertiary = ParseColor(textTertiary);
        var danger = ParseColor(profile.DangerColor);
        var dangerPressed = ParseColor(profile.DangerPressedColor);
        var success = ParseColor(profile.SuccessColor);
        var warning = ParseColor(profile.WarningColor);

        // Determine overlay tint: dark themes use white overlays, light themes use black overlays
        double bgLuminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        bool isLightTheme = bgLuminance > 0.5;
        byte overlayR = isLightTheme ? (byte)0 : (byte)255;
        byte overlayG = isLightTheme ? (byte)0 : (byte)255;
        byte overlayB = isLightTheme ? (byte)0 : (byte)255;

        SetColor("VP.Background", bg);
        SetColor("VP.BackgroundTint", Color.FromArgb((byte)(255 * GetGlassOpacity()), bg.R, bg.G, bg.B));
        SetColor("VP.BackgroundAlt", Color.FromArgb(255, bgAlt.R, bgAlt.G, bgAlt.B));
        SetColor("VP.Accent", accent);
        SetColor("VP.AccentHover", accentHover);
        SetColor("VP.TextPrimary", tPrimary);
        SetColor("VP.TextSecondary", tSecondary);
        SetColor("VP.TextTertiary", tTertiary);
        SetColor("VP.Danger", danger);
        SetColor("VP.DangerPressed", dangerPressed);
        SetColor("VP.Success", success);
        SetColor("VP.Warning", warning);
        SetColor("VP.AccentText", ReadableSemanticText(accent, bgAlt));
        SetColor("VP.AccentHoverText", ReadableSemanticText(accentHover, bgAlt));
        SetColor("VP.SuccessText", ReadableSemanticText(success, bgAlt));
        SetColor("VP.WarningText", ReadableSemanticText(warning, bgAlt));
        SetColor("VP.DangerText", ReadableSemanticText(danger, bgAlt));

        SetColor("VP.Surface", Color.FromArgb((byte)profile.SurfaceAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.SurfaceHover", Color.FromArgb((byte)profile.SurfaceHoverAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.SurfaceActive", Color.FromArgb((byte)profile.SurfaceActiveAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.SettingsCard", Color.FromArgb((byte)profile.SettingsCardAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.Card", Color.FromArgb((byte)profile.CardAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.BorderLight", Color.FromArgb((byte)profile.BorderLightAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.BorderSubtle", Color.FromArgb((byte)profile.BorderSubtleAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.Overlay", Color.FromArgb((byte)profile.OverlayAlpha, bgAlt.R, bgAlt.G, bgAlt.B));
    }

    private void ApplyGlassBackdrop()
    {
        if (_mainWindow == null)
        {
            _glassBackdropActive = false;
            return;
        }

        var background = ParseColor(_currentProfile.BackgroundColor);
        var isDark = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0 <= 0.5;
        Helpers.AcrylicHelper.SetDarkMode(_mainWindow, isDark);
        if (_currentProfile.GlassEnabled)
        {
            // 调整滑杆只更新画刷，不反复重建原生材质，避免拖动时闪烁。
            if (!_glassBackdropActive)
                _glassBackdropActive = Helpers.AcrylicHelper.TryEnableAcrylic(_mainWindow);
        }
        else
        {
            _glassBackdropActive = false;
        }
        if (!_glassBackdropActive)
            Helpers.AcrylicHelper.DisableBackdrop(_mainWindow);
    }

    public static Color ParseColor(string hex)
    {
        hex = hex.Replace("#", "");
        if (hex.Length == 6) hex = "FF" + hex;
        return Color.FromArgb(
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16),
            Convert.ToByte(hex.Substring(6, 2), 16));
    }

    public static string ToHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string AdjustAlpha(string hex, double factor)
    {
        var c = ParseColor(hex);
        var newAlpha = (byte)(c.A * factor);
        return $"#{newAlpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public double GetGlassOpacity()
    {
        // 最通透档也保留少量主题色，避免宿主底色不可见而只有设置页变色。
        return _glassBackdropActive
            ? MinimumGlassTintOpacity + (1 - MinimumGlassTintOpacity) * _currentProfile.GlassOpacity
            : 1.0;
    }
    // 语义色保留原色作背景和图标；文字单独保证与实色面板至少 4.5:1 的对比度。
    private static Color ReadableSemanticText(Color color, Color background)
    {
        static double Luminance(Color value)
        {
            static double Channel(byte channel)
            {
                var value = channel / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(value.R) + 0.7152 * Channel(value.G) + 0.0722 * Channel(value.B);
        }
        static double Contrast(double left, double right) => (Math.Max(left, right) + 0.05) / (Math.Min(left, right) + 0.05);

        var backdrop = Luminance(background);
        var target = Contrast(0, backdrop) >= Contrast(1, backdrop) ? Colors.Black : Colors.White;
        color = Color.FromRgb(color.R, color.G, color.B);
        for (var step = 0; step < 24; step++)
        {
            if (Contrast(Luminance(color), backdrop) >= 4.5) return color;
            color = Color.FromRgb(
                (byte)Math.Round(color.R + (target.R - color.R) * 0.15),
                (byte)Math.Round(color.G + (target.G - color.G) * 0.15),
                (byte)Math.Round(color.B + (target.B - color.B) * 0.15));
        }
        return target;
    }

    private sealed class ThemeColor(Color value) : INotifyPropertyChanged
    {
        private Color _value = value;
        public Color Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

}