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

    /// <summary>日志色板独立于背景拾色；空值按旧预设名称兼容，自定义旧配置默认深色。</summary>
    public bool? LogPaletteIsLight { get; set; }

    [JsonIgnore]
    public bool UsesLightLogPalette => LogPaletteIsLight
        ?? (Name == "浅色" || string.Equals(Name, "Vision Pro Light", StringComparison.OrdinalIgnoreCase));

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

    /// <summary>仅保持后台玻璃材质的活跃外观，不改变真实窗口焦点；默认遵循系统行为。</summary>
    public bool KeepGlassWhenInactive { get; set; }

    private double _glassOpacity = 0.6;

    /// <summary>背景遮色浓度；0 为最通透档、1 不透明，最低叠色由主题管理器维护。</summary>
    public double GlassOpacity
    {
        get => _glassOpacity;
        set => _glassOpacity = double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.6;
    }

    // ── Auto Contrast ────────────────────────────────────────────

    /// <summary>自动对比度始终开启；保留写入入口以兼容旧配置中的 false。</summary>
    public bool AutoTextContrast
    {
        get => true;
        set { }
    }

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
        LogPaletteIsLight = false,
        IsBuiltIn = true,
        AccentColor = "#FF2563EB",
        AccentHoverColor = "#FF3B82F6",
        BackgroundColor = "#FF0D121A",
        BackgroundAltColor = "#FF18202D",
        TextPrimaryColor = "#FFFFFFFF",
        TextSecondaryColor = "#FF9AA8BC",
        TextTertiaryColor = "#FF8796AB",
        GlassEnabled = false,
        AutoTextContrast = true,
    };

    public static ThemeProfile VisionProLight { get; } = new()
    {
        Name = "浅色",
        LogPaletteIsLight = true,
        IsBuiltIn = true,
        AccentColor = "#FF2563EB",
        AccentHoverColor = "#FF3B82F6",
        BackgroundColor = "#FFEDF2F7",
        BackgroundAltColor = "#FFFFFFFF",
        TextPrimaryColor = "#FF18202C",
        TextSecondaryColor = "#FF5E6B7A",
        TextTertiaryColor = "#FF758295",
        GlassEnabled = false,
        AutoTextContrast = true,
        SurfaceAlpha = 26,
        SurfaceHoverAlpha = 40,
        SurfaceActiveAlpha = 55,
        SettingsCardAlpha = 30,
        CardAlpha = 18,
        BorderLightAlpha = 47,
        BorderSubtleAlpha = 32,
        OverlayAlpha = 64,
    };

    /// <summary>复制配色，避免磨砂开关污染共享的内置预设。</summary>
    public ThemeProfile Clone() => (ThemeProfile)MemberwiseClone();

    public static IReadOnlyList<ThemeProfile> BuiltInPresets { get; } = new[]
    {
        VisionProDark,
        VisionProLight,
    };
}
