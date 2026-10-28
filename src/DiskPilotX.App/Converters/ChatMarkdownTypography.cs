using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace DiskPilotX.App.Converters;

/// <summary>保留可选择的原始文本，以 WPF 塑形和 Skia 彩色字形补足聊天表情显示。</summary>
internal static class ChatMarkdownTypography
{
    private static readonly FontFamily EmojiFamily = new("Segoe UI Emoji");
    private static readonly Dictionary<string, ImageBrush?> EmojiImages = new(StringComparer.Ordinal);

    internal static IEnumerable<Run> CreateRuns(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        var plain = new StringBuilder();
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (!IsEmoji(element))
            {
                plain.Append(element);
                continue;
            }
            if (plain.Length > 0)
            {
                yield return new Run(plain.ToString());
                plain.Clear();
            }

            // 每个表情使用一个完整字符簇，不能拆开肤色、ZWJ、旗帜、变体选择符和键帽。
            var run = new Run(element) { FontFamily = EmojiFamily };
            if (GetEmojiImage(element) is { } image)
            {
                run.Background = image;
                run.Foreground = Brushes.Transparent;
            }
            yield return run;
        }
        if (plain.Length > 0) yield return new Run(plain.ToString());
    }

    private static bool IsEmoji(string element)
    {
        if (element.Contains('\uFE0E') || !Rune.TryGetRuneAt(element, 0, out var rune)) return false;
        var first = rune.Value;
        return Contains(EmojiPresentation, first) ||
            (element.Contains('\uFE0F') && Contains(EmojiCharacters, first)) ||
            (element.EndsWith('\u20E3') && (first is >= '0' and <= '9' or '#' or '*'));
    }

    private static bool Contains(int[] ranges, int value)
    {
        for (var i = 0; i < ranges.Length; i += 2)
        {
            if (value < ranges[i]) return false;
            if (value <= ranges[i + 1]) return true;
        }
        return false;
    }

    private static ImageBrush? GetEmojiImage(string element)
    {
        lock (EmojiImages)
        {
            if (EmojiImages.TryGetValue(element, out var cached)) return cached;
            ImageBrush? image;
            try { image = RenderEmoji(element); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or
                System.IO.IOException or DllNotFoundException or EntryPointNotFoundException)
            {
                Trace.TraceWarning("聊天表情彩色绘制不可用，保留原始文本：{0}", error.Message);
                image = null;
            }
            // ponytail: 只缓存有限的常见表情，避免长会话或不可信文本导致无界位图占用。
            if (EmojiImages.Count < 256) EmojiImages.Add(element, image);
            return image;
        }
    }

    private static ImageBrush? RenderEmoji(string element)
    {
        const double emSize = 64;
        var text = new FormattedText(element, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(EmojiFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            emSize, Brushes.Black, 1);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) context.DrawText(text, new Point(0, 0));
        var glyphRuns = GlyphRuns(drawing).ToArray();
        if (glyphRuns.Length == 0 || glyphRuns.Any(run => run.GlyphIndices.Contains((ushort)0))) return null;

        // WPF 负责 GSUB/GPOS 塑形；不能将 ZWJ 文本直接交给只做字符映射的 SKCanvas.DrawText。
        // 官方实现依据：dotnet/wpf 的 FormattedText.Draw、mono/SkiaSharp 的 SKTextBlob.CreatePositioned。
        var width = (int)Math.Ceiling(Math.Max(text.WidthIncludingTrailingWhitespace, drawing.Bounds.Right));
        var height = (int)Math.Ceiling(Math.Max(text.Height, drawing.Bounds.Bottom));
        if (width <= 0 || height <= 0 || width > 2048 || height > 256) return null;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        foreach (var run in glyphRuns)
        {
            if (!run.GlyphTypeface.FontUri.IsFile) return null;
            using var face = SKTypeface.FromFile(run.GlyphTypeface.FontUri.LocalPath);
            if (face == null) return null;
            using var font = new SKFont(face, (float)run.FontRenderingEmSize);
            var glyphs = run.GlyphIndices.ToArray();
            var positions = new SKPoint[glyphs.Length];
            var x = run.BaselineOrigin.X;
            for (var i = 0; i < glyphs.Length; i++)
            {
                var offset = run.GlyphOffsets is { Count: > 0 } ? run.GlyphOffsets[i] : default;
                positions[i] = new SKPoint((float)(x + offset.X), (float)(run.BaselineOrigin.Y - offset.Y));
                x += run.AdvanceWidths[i];
            }
            using var blob = SKTextBlob.CreatePositioned(MemoryMarshal.AsBytes(glyphs.AsSpan()), SKTextEncoding.GlyphId, font, positions);
            if (blob == null) return null;
            canvas.DrawText(blob, 0, 0, paint);
        }
        canvas.Flush();
        var pixels = bitmap.Bytes;
        // 字体不支持的旗帜等组合保留可读文本，不能用字体名称或灰阶位图冒充彩色成功。
        var hasColor = false;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 100 && Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) -
                Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2])) > 35)
            {
                hasColor = true;
                break;
            }
        if (!hasColor) return null;
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, bitmap.RowBytes);
        source.Freeze();
        var brush = new ImageBrush(source) { Stretch = Stretch.Uniform };
        brush.Freeze();
        return brush;
    }

    private static IEnumerable<GlyphRun> GlyphRuns(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var run in GlyphRuns(child)) yield return run;
    }

    // Unicode 17 官方 Emoji 属性表；仅合并相邻区间，不用大范围符号区段猜测表情。
    // 来源：https://www.unicode.org/Public/17.0.0/ucd/emoji/emoji-data.txt
    private static readonly int[] EmojiPresentation =
    [
        0x231A, 0x231B, 0x23E9, 0x23EC, 0x23F0, 0x23F0, 0x23F3, 0x23F3,
        0x25FD, 0x25FE, 0x2614, 0x2615, 0x2648, 0x2653, 0x267F, 0x267F,
        0x2693, 0x2693, 0x26A1, 0x26A1, 0x26AA, 0x26AB, 0x26BD, 0x26BE,
        0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4, 0x26EA, 0x26EA,
        0x26F2, 0x26F3, 0x26F5, 0x26F5, 0x26FA, 0x26FA, 0x26FD, 0x26FD,
        0x2705, 0x2705, 0x270A, 0x270B, 0x2728, 0x2728, 0x274C, 0x274C,
        0x274E, 0x274E, 0x2753, 0x2755, 0x2757, 0x2757, 0x2795, 0x2797,
        0x27B0, 0x27B0, 0x27BF, 0x27BF, 0x2B1B, 0x2B1C, 0x2B50, 0x2B50,
        0x2B55, 0x2B55, 0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF, 0x1F18E, 0x1F18E,
        0x1F191, 0x1F19A, 0x1F1E6, 0x1F1FF, 0x1F201, 0x1F201, 0x1F21A, 0x1F21A,
        0x1F22F, 0x1F22F, 0x1F232, 0x1F236, 0x1F238, 0x1F23A, 0x1F250, 0x1F251,
        0x1F300, 0x1F320, 0x1F32D, 0x1F335, 0x1F337, 0x1F37C, 0x1F37E, 0x1F393,
        0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0, 0x1F3F4, 0x1F3F4,
        0x1F3F8, 0x1F43E, 0x1F440, 0x1F440, 0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D,
        0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F57A, 0x1F57A, 0x1F595, 0x1F596,
        0x1F5A4, 0x1F5A4, 0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC,
        0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D8, 0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC,
        0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0, 0x1F90C, 0x1F93A,
        0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA8A,
        0x1FA8E, 0x1FAC6, 0x1FAC8, 0x1FAC8, 0x1FACD, 0x1FADC, 0x1FADF, 0x1FAEA,
        0x1FAEF, 0x1FAF8,
    ];

    private static readonly int[] EmojiCharacters =
    [
        0x23, 0x23, 0x2A, 0x2A, 0x30, 0x39, 0xA9, 0xA9,
        0xAE, 0xAE, 0x203C, 0x203C, 0x2049, 0x2049, 0x2122, 0x2122,
        0x2139, 0x2139, 0x2194, 0x2199, 0x21A9, 0x21AA, 0x231A, 0x231B,
        0x2328, 0x2328, 0x23CF, 0x23CF, 0x23E9, 0x23F3, 0x23F8, 0x23FA,
        0x24C2, 0x24C2, 0x25AA, 0x25AB, 0x25B6, 0x25B6, 0x25C0, 0x25C0,
        0x25FB, 0x25FE, 0x2600, 0x2604, 0x260E, 0x260E, 0x2611, 0x2611,
        0x2614, 0x2615, 0x2618, 0x2618, 0x261D, 0x261D, 0x2620, 0x2620,
        0x2622, 0x2623, 0x2626, 0x2626, 0x262A, 0x262A, 0x262E, 0x262F,
        0x2638, 0x263A, 0x2640, 0x2640, 0x2642, 0x2642, 0x2648, 0x2653,
        0x265F, 0x2660, 0x2663, 0x2663, 0x2665, 0x2666, 0x2668, 0x2668,
        0x267B, 0x267B, 0x267E, 0x267F, 0x2692, 0x2697, 0x2699, 0x2699,
        0x269B, 0x269C, 0x26A0, 0x26A1, 0x26A7, 0x26A7, 0x26AA, 0x26AB,
        0x26B0, 0x26B1, 0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26C8, 0x26C8,
        0x26CE, 0x26CF, 0x26D1, 0x26D1, 0x26D3, 0x26D4, 0x26E9, 0x26EA,
        0x26F0, 0x26F5, 0x26F7, 0x26FA, 0x26FD, 0x26FD, 0x2702, 0x2702,
        0x2705, 0x2705, 0x2708, 0x270D, 0x270F, 0x270F, 0x2712, 0x2712,
        0x2714, 0x2714, 0x2716, 0x2716, 0x271D, 0x271D, 0x2721, 0x2721,
        0x2728, 0x2728, 0x2733, 0x2734, 0x2744, 0x2744, 0x2747, 0x2747,
        0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755, 0x2757, 0x2757,
        0x2763, 0x2764, 0x2795, 0x2797, 0x27A1, 0x27A1, 0x27B0, 0x27B0,
        0x27BF, 0x27BF, 0x2934, 0x2935, 0x2B05, 0x2B07, 0x2B1B, 0x2B1C,
        0x2B50, 0x2B50, 0x2B55, 0x2B55, 0x3030, 0x3030, 0x303D, 0x303D,
        0x3297, 0x3297, 0x3299, 0x3299, 0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF,
        0x1F170, 0x1F171, 0x1F17E, 0x1F17F, 0x1F18E, 0x1F18E, 0x1F191, 0x1F19A,
        0x1F1E6, 0x1F1FF, 0x1F201, 0x1F202, 0x1F21A, 0x1F21A, 0x1F22F, 0x1F22F,
        0x1F232, 0x1F23A, 0x1F250, 0x1F251, 0x1F300, 0x1F321, 0x1F324, 0x1F393,
        0x1F396, 0x1F397, 0x1F399, 0x1F39B, 0x1F39E, 0x1F3F0, 0x1F3F3, 0x1F3F5,
        0x1F3F7, 0x1F4FD, 0x1F4FF, 0x1F53D, 0x1F549, 0x1F54E, 0x1F550, 0x1F567,
        0x1F56F, 0x1F570, 0x1F573, 0x1F57A, 0x1F587, 0x1F587, 0x1F58A, 0x1F58D,
        0x1F590, 0x1F590, 0x1F595, 0x1F596, 0x1F5A4, 0x1F5A5, 0x1F5A8, 0x1F5A8,
        0x1F5B1, 0x1F5B2, 0x1F5BC, 0x1F5BC, 0x1F5C2, 0x1F5C4, 0x1F5D1, 0x1F5D3,
        0x1F5DC, 0x1F5DE, 0x1F5E1, 0x1F5E1, 0x1F5E3, 0x1F5E3, 0x1F5E8, 0x1F5E8,
        0x1F5EF, 0x1F5EF, 0x1F5F3, 0x1F5F3, 0x1F5FA, 0x1F64F, 0x1F680, 0x1F6C5,
        0x1F6CB, 0x1F6D2, 0x1F6D5, 0x1F6D8, 0x1F6DC, 0x1F6E5, 0x1F6E9, 0x1F6E9,
        0x1F6EB, 0x1F6EC, 0x1F6F0, 0x1F6F0, 0x1F6F3, 0x1F6FC, 0x1F7E0, 0x1F7EB,
        0x1F7F0, 0x1F7F0, 0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF,
        0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA8A, 0x1FA8E, 0x1FAC6, 0x1FAC8, 0x1FAC8,
        0x1FACD, 0x1FADC, 0x1FADF, 0x1FAEA, 0x1FAEF, 0x1FAF8,
    ];

}
