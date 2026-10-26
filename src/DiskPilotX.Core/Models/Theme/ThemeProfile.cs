using System.Text.Json.Serialization;

namespace DiskPilotX.Core.Models.Theme;

/// <summary>
/// Theme visual profile — controls accent color, glass material settings,
/// Gaussian blur, transparency, and text contrast behavior.
/// </summary>
public class ThemeProfile
{
    /// <summary>Display name for the preset (e.g. "Vision Pro Dark").</summary>
    public string Name { get; set; } = "Vision Pro Dark";

    /// <summary>Whether this is a built-in preset (not user-editable).</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; } = true;

    // ── Primary Palette ──────────────────────────────────────────

    /// <summary>Accent color as hex ARGB (e.g. "#FF2563EB").</summary>
    public string AccentColor { get; set; } = "#FF2563EB";

    /// <summary>Accent hover color.</summary>
    public string AccentHoverColor { get; set; } = "#FF3B82F6";

    /// <summary>Background color.</summary>
    public string BackgroundColor { get; set; } = "#FF0D121A";

    /// <summary>Alternative background (cards, panels).</summary>
    public string BackgroundAltColor { get; set; } = "#FF18202D";

    // ── Text Colors ──────────────────────────────────────────────

    public string TextPrimaryColor { get; set; } = "#FFFFFFFF";
    public string TextSecondaryColor { get; set; } = "#FF9AA8BC";
    public string TextTertiaryColor { get; set; } = "#FF8796AB";

    // ── Glass & Blur ─────────────────────────────────────────────

    /// <summary>Enable frosted-glass (acrylic/mica) on the window backdrop.</summary>
    public bool GlassEnabled { get; set; } = false;

    /// <summary>Glass backdrop type: 0=None, 1=Mica, 2=Acrylic, 3=Tabbed.</summary>
    public int GlassBackdropType { get; set; } = 0;

    /// <summary>Gaussian blur radius for in-app glass panels (0 = off).</summary>
    public double BlurRadius { get; set; } = 0;

    /// <summary>Glass surface opacity (0.0 = fully transparent, 1.0 = fully opaque).</summary>
    public double GlassOpacity { get; set; } = 0.85;

    // ── Auto Contrast ────────────────────────────────────────────

    /// <summary>When true, text colors are auto-computed from background luminance.</summary>
    public bool AutoTextContrast { get; set; } = false;

    // ── Semantic Colors ──────────────────────────────────────────

    public string DangerColor { get; set; } = "#FFFF453A";
    public string DangerPressedColor { get; set; } = "#FFCC1A1A";
    public string SuccessColor { get; set; } = "#FF30D158";
    public string WarningColor { get; set; } = "#FFFFD60A";

    /// <summary>Surface alpha levels (0-255) for layered transparency.</summary>
    public int SurfaceAlpha { get; set; } = 26;
    public int SurfaceHoverAlpha { get; set; } = 34;
    public int SurfaceActiveAlpha { get; set; } = 43;
    public int SettingsCardAlpha { get; set; } = 30;
    public int CardAlpha { get; set; } = 18;
    public int BorderLightAlpha { get; set; } = 47;
    public int BorderSubtleAlpha { get; set; } = 32;
    public int OverlayAlpha { get; set; } = 128;

    // ── Presets ──────────────────────────────────────────────────

    public static ThemeProfile VisionProDark { get; } = new()
    {
        Name = "深色",
        IsBuiltIn = true,
        AccentColor = "#FF2563EB",
        AccentHoverColor = "#FF3B82F6",
        BackgroundColor = "#FF0D121A",
        BackgroundAltColor = "#FF18202D",
        TextPrimaryColor = "#FFFFFFFF",
        TextSecondaryColor = "#FF9AA8BC",
        TextTertiaryColor = "#FF8796AB",
        GlassEnabled = false,
        GlassBackdropType = 0,
        BlurRadius = 0,
        GlassOpacity = 0.85,
        AutoTextContrast = false,
    };

    public static ThemeProfile VisionProLight { get; } = new()
    {
        Name = "浅色",
        IsBuiltIn = true,
        AccentColor = "#FF2563EB",
        AccentHoverColor = "#FF3B82F6",
        BackgroundColor = "#FFEDF2F7",
        BackgroundAltColor = "#FFFFFFFF",
        TextPrimaryColor = "#FF18202C",
        TextSecondaryColor = "#FF5E6B7A",
        TextTertiaryColor = "#FF758295",
        GlassEnabled = false,
        GlassBackdropType = 0,
        BlurRadius = 0,
        GlassOpacity = 0.9,
        AutoTextContrast = false,
        SurfaceAlpha = 26,
        SurfaceHoverAlpha = 40,
        SurfaceActiveAlpha = 55,
        SettingsCardAlpha = 30,
        CardAlpha = 18,
        BorderLightAlpha = 47,
        BorderSubtleAlpha = 32,
        OverlayAlpha = 64,
    };

    public static ThemeProfile FrostedGlass { get; } = new()
    {
        Name = "毛玻璃",
        IsBuiltIn = true,
        AccentColor = "#FF4A90D9",
        AccentHoverColor = "#FF5DA3E8",
        BackgroundColor = "#FF1E1E2E",
        BackgroundAltColor = "#FF2A2A3C",
        TextPrimaryColor = "#FFEDEDF5",
        TextSecondaryColor = "#FFB0B0C0",
        TextTertiaryColor = "#FF888899",
        GlassEnabled = true,
        GlassBackdropType = 3, // Acrylic
        BlurRadius = 30,
        GlassOpacity = 0.7,
        AutoTextContrast = true,
        SurfaceAlpha = 40,
        SurfaceHoverAlpha = 60,
        SurfaceActiveAlpha = 80,
        SettingsCardAlpha = 45,
        CardAlpha = 30,
        BorderLightAlpha = 60,
        BorderSubtleAlpha = 40,
        OverlayAlpha = 80,
    };

    public static IReadOnlyList<ThemeProfile> BuiltInPresets { get; } = new[]
    {
        VisionProDark,
        VisionProLight,
        FrostedGlass,
    };
}
