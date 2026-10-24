using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SpaceMonger.Core.Models.Theme;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.Services;

/// <summary>
/// Central theme manager — applies ThemeProfile to the WPF resource dictionary at runtime.
/// Handles color injection, blur effects, glass backdrop, and text contrast auto-computation.
/// </summary>
public class ThemeManager
{
    private readonly ISettingsService _settingsService;
    private ThemeProfile _currentProfile;
    private Window? _mainWindow;

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
        ApplyGlassBackdrop();
    }

    public void ApplyTheme(ThemeProfile profile)
    {
        _currentProfile = profile;
        ApplyToResources();
        ApplyGlassBackdrop();
        Persist();
        ThemeChanged?.Invoke();
    }

    public void Refresh()
    {
        ApplyToResources();
        ApplyGlassBackdrop();
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

        void SetBrush(string key, Color color)
        {
            // 复用可变画刷，使现有 Markdown 文档也能跟随明暗主题切换。
            if (app.Resources[key] is SolidColorBrush existing && !existing.IsFrozen) existing.Color = color;
            else app.Resources[key] = new SolidColorBrush(color);
        }

        void SetColor(string key, Color color)
        {
            app.Resources[key] = color;
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
        SetColor("VP.BackgroundAlt", bgAlt);
        SetColor("VP.Accent", accent);
        SetColor("VP.AccentHover", accentHover);
        SetColor("VP.TextPrimary", tPrimary);
        SetColor("VP.TextSecondary", tSecondary);
        SetColor("VP.TextTertiary", tTertiary);
        SetColor("VP.Danger", danger);
        SetColor("VP.DangerPressed", dangerPressed);
        SetColor("VP.Success", success);
        SetColor("VP.Warning", warning);

        SetColor("VP.Surface", Color.FromArgb((byte)profile.SurfaceAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.SurfaceHover", Color.FromArgb((byte)profile.SurfaceHoverAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.SurfaceActive", Color.FromArgb((byte)profile.SurfaceActiveAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.SettingsCard", Color.FromArgb((byte)profile.SettingsCardAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.Card", Color.FromArgb((byte)profile.CardAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.BorderLight", Color.FromArgb((byte)profile.BorderLightAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.BorderSubtle", Color.FromArgb((byte)profile.BorderSubtleAlpha, overlayR, overlayG, overlayB));
        SetColor("VP.Overlay", Color.FromArgb((byte)profile.OverlayAlpha, bgAlt.R, bgAlt.G, bgAlt.B));

        // When glass is enabled, make backgrounds semi-transparent so the system backdrop shows through
        var bgOpacity = profile.GlassEnabled ? profile.GlassOpacity : 1.0;
        var bgWithOpacity = Color.FromArgb((byte)(bg.A * bgOpacity), bg.R, bg.G, bg.B);
        var bgAltWithOpacity = Color.FromArgb((byte)(bgAlt.A * bgOpacity), bgAlt.R, bgAlt.G, bgAlt.B);

        SetBrush("VP.BackgroundBrush", bgWithOpacity);
        SetBrush("VP.BackgroundAltBrush", bgAltWithOpacity);
        SetBrush("VP.SurfaceBrush", Color.FromArgb((byte)profile.SurfaceAlpha, overlayR, overlayG, overlayB));
        SetBrush("VP.SurfaceHoverBrush", Color.FromArgb((byte)profile.SurfaceHoverAlpha, overlayR, overlayG, overlayB));
        SetBrush("VP.SurfaceActiveBrush", Color.FromArgb((byte)profile.SurfaceActiveAlpha, overlayR, overlayG, overlayB));
        SetBrush("VP.SettingsCardBrush", Color.FromArgb((byte)profile.SettingsCardAlpha, overlayR, overlayG, overlayB));
        SetBrush("VP.CardBrush", Color.FromArgb((byte)profile.CardAlpha, overlayR, overlayG, overlayB));
        SetBrush("VP.BorderLightBrush", Color.FromArgb((byte)profile.BorderLightAlpha, overlayR, overlayG, overlayB));
        SetBrush("VP.BorderSubtleBrush", Color.FromArgb((byte)profile.BorderSubtleAlpha, overlayR, overlayG, overlayB));
        SetBrush("VP.TextPrimaryBrush", tPrimary);
        SetBrush("VP.TextSecondaryBrush", tSecondary);
        SetBrush("VP.TextTertiaryBrush", tTertiary);
        SetBrush("VP.AccentBrush", accent);
        SetBrush("VP.AccentHoverBrush", accentHover);
        SetBrush("VP.DangerBrush", danger);
        SetBrush("VP.DangerPressedBrush", dangerPressed);
        SetBrush("VP.SuccessBrush", success);
        SetBrush("VP.WarningBrush", warning);
        SetBrush("VP.OverlayBrush", Color.FromArgb((byte)profile.OverlayAlpha, bgAlt.R, bgAlt.G, bgAlt.B));

        UpdateGlassResources(profile);
    }

    private void UpdateGlassResources(ThemeProfile profile)
    {
        var app = Application.Current;
        if (app == null) return;

        if (app.Resources.Contains("VP.GlassOpacity"))
            app.Resources["VP.GlassOpacity"] = profile.GlassOpacity;
        else
            app.Resources.Add("VP.GlassOpacity", profile.GlassOpacity);

        if (app.Resources.Contains("VP.BlurRadius"))
            app.Resources["VP.BlurRadius"] = profile.BlurRadius;
        else
            app.Resources.Add("VP.BlurRadius", profile.BlurRadius);

        if (app.Resources.Contains("VP.GlassEnabled"))
            app.Resources["VP.GlassEnabled"] = profile.GlassEnabled;
        else
            app.Resources.Add("VP.GlassEnabled", profile.GlassEnabled);
    }

    private void ApplyGlassBackdrop()
    {
        if (_mainWindow == null) return;
        var profile = _currentProfile;

        if (!profile.GlassEnabled || profile.GlassBackdropType == 0)
        {
            Helpers.AcrylicHelper.DisableBackdrop(_mainWindow);
            return;
        }

        switch (profile.GlassBackdropType)
        {
            case 1: Helpers.AcrylicHelper.EnableMica(_mainWindow); break;
            case 2: Helpers.AcrylicHelper.EnableAcrylic(_mainWindow); break;
            case 3: Helpers.AcrylicHelper.EnableAcrylic(_mainWindow); break;
        }
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

    public BlurEffect? CreateBlurEffect()
    {
        if (_currentProfile.BlurRadius <= 0) return null;
        return new BlurEffect
        {
            Radius = _currentProfile.BlurRadius,
            KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Quality,
        };
    }

    public double GetGlassOpacity()
    {
        return _currentProfile.GlassEnabled ? _currentProfile.GlassOpacity : 1.0;
    }
}