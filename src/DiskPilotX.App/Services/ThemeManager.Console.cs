using System.Windows.Media;

namespace DiskPilotX.App.Services;

public partial class ThemeManager
{
    private void ApplyConsoleResources(Action<string, Color> setBrush)
    {
        // 日志只认明确选择的深浅色板，不响应自定义背景、强调色或 UI 自动对比。
        var profile = _currentProfile.UsesLightLogPalette
            ? DiskPilotX.Core.Models.Theme.ThemeProfile.VisionProLight
            : DiskPilotX.Core.Models.Theme.ThemeProfile.VisionProDark;
        var background = ParseColor(profile.BackgroundColor);
        var light = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0 > 0.5;
        var overlay = light ? (byte)0 : (byte)255;
        var surface = Color.FromArgb((byte)profile.SurfaceAlpha, overlay, overlay, overlay);
        var panelBackground = ConsoleOver(surface, background);

        // 不加深控制台背景；玻璃只提高文字目标对比，实际效果仍取决于桌面背景。
        var targetContrast = _glassBackdropActive ? 7.0 : 4.5;
        void SetText(string key, Color color) => setBrush(key, ReadableSemanticText(color, panelBackground, targetContrast));

        // 日志色系独立于用户 UI 强调色，深浅背景只校正明暗；深色普通模式保留原色。
        SetText("VP.LogVerboseBrush", Color.FromRgb(122, 162, 247));
        SetText("VP.LogDebugBrush", Color.FromRgb(187, 154, 247));
        SetText("VP.LogInfoBrush", Color.FromRgb(158, 206, 106));
        SetText("VP.LogWarningBrush", Color.FromRgb(224, 175, 104));
        SetText("VP.LogErrorBrush", Color.FromRgb(247, 118, 142));
        SetText("VP.LogWarningMessageBrush", Color.FromRgb(255, 199, 119));
        SetText("VP.LogErrorMessageBrush", Color.FromRgb(255, 158, 171));
        SetText("VP.LogMessageBrush", ParseColor(profile.TextPrimaryColor));
        SetText("VP.LogTimeBrush", ParseColor(profile.TextTertiaryColor));
    }

    private static Color ConsoleOver(Color foreground, Color background)
    {
        var alpha = foreground.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }
}
