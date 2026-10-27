using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DiskPilotX.App.Converters;

/// <summary>
/// 把自定义围栏代码块渲染为 WPF 原生图表，全部使用 VisionPro 主题画刷（VP.*）。
/// 让 AI 用结构化数据「长出」条形图 / 徽章 / 进度条，而不是写文字段落。
///
/// 支持的围栏语言标识：
///   disk-bar    每行「标签 | 数值」，按数值画横向条形图（用于 Top N 大小对比）。
///   disk-badge  每行「标签 | 文本」，渲染为标签 + 徽章（用于分类统计 / 安全等级）。
///   disk-meter  每行「标签 | 0~100」，渲染为带标签的进度条（用于占比 / 百分比）。
/// </summary>
internal static class MarkdownChartRenderer
{
    private const string BarLang = "disk-bar";
    private const string BadgeLang = "disk-badge";
    private const string MeterLang = "disk-meter";

    /// <summary>识别自定义图表语言标识；不匹配返回 null，走普通代码块渲染。</summary>
    public static bool IsChartLanguage(string? info) =>
        info is not null && (info.Equals(BarLang, StringComparison.OrdinalIgnoreCase)
            || info.Equals(BadgeLang, StringComparison.OrdinalIgnoreCase)
            || info.Equals(MeterLang, StringComparison.OrdinalIgnoreCase));

    /// <summary>把围栏块内容渲染为图表 Block（BlockUIContainer）。</summary>
    public static Block Render(string? info, string content)
    {
        var lang = info?.Trim().ToLowerInvariant();
        return lang switch
        {
            BarLang => new BlockUIContainer(CreateBarChart(content)),
            BadgeLang => new BlockUIContainer(CreateBadgeList(content)),
            MeterLang => new BlockUIContainer(CreateMeterList(content)),
            _ => CreateCodeBlockFallback(content)
        };
    }

    // ── 主题画刷查找（与 MarkdownToFlowDocumentConverter 一致的回退策略）──────────
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Brush> FallbackBrushes = new();

    private static Brush Brush(string key, Color fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as Brush
           ?? FallbackBrushes.GetOrAdd(key, _ => { var b = new SolidColorBrush(fallback); b.Freeze(); return b; });

    private static Brush Accent => Brush("VP.AccentBrush", Color.FromRgb(0x25, 0x63, 0xEB));
    private static Brush Success => Brush("VP.SuccessBrush", Color.FromRgb(0x30, 0xD1, 0x58));
    private static Brush Warning => Brush("VP.WarningBrush", Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static Brush Danger => Brush("VP.DangerBrush", Color.FromRgb(0xFF, 0x45, 0x3A));
    private static Brush TextPrimary => Brush("VP.TextPrimaryBrush", Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static Brush TextSecondary => Brush("VP.TextSecondaryBrush", Color.FromRgb(0x9A, 0xA8, 0xBC));
    private static Brush SurfaceHover => Brush("VP.SurfaceHoverBrush", Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static Brush Surface => Brush("VP.SurfaceBrush", Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static Brush BorderSubtle => Brush("VP.BorderSubtleBrush", Color.FromRgb(0x44, 0x44, 0x44));

    private static readonly FontFamily UiFont = new("Segoe UI, Microsoft YaHei UI");
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, monospace");

    /// <summary>按语义关键字把纯文本值映射为主题色（安全=绿、警告=黄、危险=红，其余强调色）。</summary>
    private static Brush ColorForValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Accent;
        var v = value.Trim();
        if (v is "Safe" or "安全" or "可清理" or "已清理" or "成功" or "低" or "Low") return Success;
        if (v is "ReviewFirst" or "Caution" or "警告" or "谨慎" or "注意" or "中" or "Medium") return Warning;
        if (v is "危险" or "删除" or "高" or "High" or "失败") return Danger;
        return Accent;
    }

    // ── disk-bar：横向条形图 ───────────────────────────────────────────────
    private static Border CreateBarChart(string content)
    {
        var rows = ParseRows(content);
        if (rows.Count == 0) return CreateEmptyChart("条形图无数据");

        double maxValue = 0;
        foreach (var (_, value) in rows)
            if (value > maxValue) maxValue = value;

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };

        // 标签列与条形列用共享比例对齐：数值文本固定列宽
        foreach (var (label, value) in rows)
        {
            var ratio = maxValue <= 0 ? 0 : value / maxValue;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelText = new TextBlock
            {
                Text = label,
                FontFamily = UiFont,
                FontSize = 12,
                Foreground = TextSecondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(labelText, 0);
            row.Children.Add(labelText);

            // 条形背景 + 前景
            var track = new Border
            {
                Height = 16,
                CornerRadius = new CornerRadius(3),
                Background = SurfaceHover,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true,
            };
            var barFill = new Border
            {
                Background = Accent,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = ratio * 100, // 用星号列 + 百分比宽度近似
            };
            // 用 Grid 包裹，让条按比例占满
            var barHost = new Grid();
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star) });
            var fill = new Border { Background = Accent, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 1, 0, 1) };
            Grid.SetColumn(fill, 0);
            barHost.Children.Add(fill);
            track.Child = barHost;
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var valueText = new TextBlock
            {
                Text = FormatValue(value),
                FontFamily = MonoFont,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);

            row.Margin = new Thickness(0, 2, 0, 2);
            panel.Children.Add(row);
        }

        return new Border
        {
            Background = Surface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Child = panel,
        };
    }

    // ── disk-badge：徽章行 ───────────────────────────────────────────────
    private static Border CreateBadgeList(string content)
    {
        var rows = ParseTextRows(content);
        if (rows.Count == 0) return CreateEmptyChart("徽章无数据");

        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        foreach (var (label, value) in rows)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var labelText = new TextBlock
            {
                Text = label,
                FontFamily = UiFont,
                FontSize = 12,
                Foreground = TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            DockPanel.SetDock(labelText, Dock.Left);
            row.Children.Add(labelText);

            var badge = new Border
            {
                Background = ColorForValue(value),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            badge.Child = new TextBlock
            {
                Text = value,
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
            };
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);

            panel.Children.Add(row);
        }

        return new Border
        {
            Background = Surface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
    }

    // ── disk-meter：进度条 ───────────────────────────────────────────────
    private static Border CreateMeterList(string content)
    {
        var rows = ParseRows(content);
        if (rows.Count == 0) return CreateEmptyChart("进度条无数据");

        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        foreach (var (label, value) in rows)
        {
            var clamped = Math.Clamp(value, 0, 100);

            var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headerLabel = new TextBlock
            {
                Text = label,
                FontFamily = UiFont,
                FontSize = 12,
                Foreground = TextSecondary,
            };
            Grid.SetColumn(headerLabel, 0);
            header.Children.Add(headerLabel);
            var headerValue = new TextBlock
            {
                Text = value.ToString("0.#", CultureInfo.InvariantCulture) + "%",
                FontFamily = MonoFont,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(headerValue, 1);
            header.Children.Add(headerValue);

            var track = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = SurfaceHover,
                ClipToBounds = true,
            };
            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clamped, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 100 - clamped), GridUnitType.Star) });
            var fill = new Border
            {
                Background = MeterColor(clamped),
                CornerRadius = new CornerRadius(4),
            };
            Grid.SetColumn(fill, 0);
            host.Children.Add(fill);
            track.Child = host;

            var group = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            group.Children.Add(header);
            group.Children.Add(track);
            panel.Children.Add(group);
        }

        return new Border
        {
            Background = Surface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
    }

    private static Brush MeterColor(double pct) =>
        pct >= 80 ? Danger : pct >= 50 ? Warning : Success;

    // ── 兜底与解析 ───────────────────────────────────────────────────────
    private static Block CreateCodeBlockFallback(string content)
    {
        var container = new Border
        {
            Background = Surface,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
        };
        container.Child = new TextBlock
        {
            Text = content,
            FontFamily = MonoFont,
            FontSize = 12.5,
            Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap,
        };
        return new BlockUIContainer(container);
    }

    private static Border CreateEmptyChart(string message)
    {
        var b = new Border
        {
            Background = Surface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
        };
        b.Child = new TextBlock
        {
            Text = message,
            FontFamily = UiFont,
            FontSize = 12,
            Foreground = TextSecondary,
            FontStyle = FontStyles.Italic,
        };
        return b;
    }

    /// <summary>解析「标签 | 数值」行；数值列必须是数字（可为百分比数字）。</summary>
    private static List<(string Label, double Value)> ParseRows(string content)
    {
        var result = new List<(string, double)>();
        foreach (var line in SplitLines(content))
        {
            var parts = SplitPipe(line);
            if (parts.Length < 2) continue;
            if (!TryParseNumber(parts[^1], out var value)) continue;
            result.Add((parts[0].Trim(), value));
        }
        return result;
    }

    /// <summary>解析「标签 | 文本」行，文本列不强制为数字。</summary>
    private static List<(string Label, string Value)> ParseTextRows(string content)
    {
        var result = new List<(string, string)>();
        foreach (var line in SplitLines(content))
        {
            var parts = SplitPipe(line);
            if (parts.Length < 2) continue;
            if (string.IsNullOrWhiteSpace(parts[1])) continue;
            result.Add((parts[0].Trim(), parts[1].Trim()));
        }
        return result;
    }

    private static IEnumerable<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string[] SplitPipe(string line)
    {
        // 去掉首尾竖线（对齐用），再按竖线切分
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|');
    }

    private static bool TryParseNumber(string text, out double value)
    {
        text = text.Trim().TrimEnd('%');
        // 去掉千分位分隔符
        text = text.Replace(",", "").Replace("，", "");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatValue(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
