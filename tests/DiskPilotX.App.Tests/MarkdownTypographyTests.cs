using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiskPilotX.App.Converters;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class MarkdownTypographyTests
{
    private static FlowDocument Parse(string markdown) => (FlowDocument)new MarkdownToFlowDocumentConverter()
        .Convert(markdown, typeof(FlowDocument), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void 行内代码保留原文且与正文共享基线() => StaHost.Run(() =>
    {
        var document = Parse("中文 `D:\\SteamLibrary\\游戏` English");
        var paragraph = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
        Assert.DoesNotContain(paragraph.Inlines.Cast<Inline>(), inline => inline is InlineUIContainer);
        var code = Assert.Single(paragraph.Inlines.OfType<Span>());
        Assert.Equal(BaselineAlignment.Baseline, code.BaselineAlignment);
        Assert.Equal("D:\\SteamLibrary\\游戏", new TextRange(code.ContentStart, code.ContentEnd).Text);
        Assert.Contains("中文 D:\\SteamLibrary\\游戏 English", new TextRange(document.ContentStart, document.ContentEnd).Text);
    });

    [Fact]
    public void 正文标题引用和列表不继承两端拉伸() => StaHost.Run(() =>
    {
        var document = Parse("# 标题 English\n\n中文 English 正文\n\n- 中文 English 列表\n\n> 引用 English");
        Assert.Equal(TextAlignment.Left, document.TextAlignment);
        Assert.Equal(TextAlignment.Left, document.ReadLocalValue(FlowDocument.TextAlignmentProperty));
        Assert.False(document.IsHyphenationEnabled);
        Assert.All(段落(document.Blocks), paragraph => Assert.Equal(TextAlignment.Left, paragraph.TextAlignment));
    });

    [Fact]
    public void 有序列表保留起始编号及嵌套内容() => StaHost.Run(() =>
    {
        var document = Parse("3. 中文 English\n   - 嵌套文字\n4. 下一项");
        var list = Assert.IsType<System.Windows.Documents.List>(document.Blocks.FirstBlock);
        Assert.Equal(3, list.StartIndex);
        Assert.Equal(TextMarkerStyle.Decimal, list.MarkerStyle);
        Assert.Equal(2, list.ListItems.Count);
        Assert.Contains("嵌套文字", new TextRange(document.ContentStart, document.ContentEnd).Text);
    });

    [Fact]
    public void 长路径能换行并完整复制() => StaHost.Run(() =>
    {
        const string path = @"D:\SteamLibrary\steamapps\common\这是一个非常长的游戏目录\SomeExtremelyLongUnbrokenDirectoryName1234567890\Content\文件.bin";
        var document = Parse($"路径 `{path}` 结束");
        document.PagePadding = new Thickness(0);
        var viewer = new RichTextBox { Document = document, Width = 210, Height = 500, Padding = new Thickness(0) };
        viewer.Measure(new Size(210, 500));
        viewer.Arrange(new Rect(0, 0, 210, 500));
        viewer.UpdateLayout();
        Assert.Contains(path, new TextRange(document.ContentStart, document.ContentEnd).Text);
        var paragraph = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
        var code = Assert.Single(paragraph.Inlines.OfType<Span>());
        var start = code.ContentStart.GetPositionAtOffset(1)!;
        var end = code.ContentEnd.GetPositionAtOffset(-1)!;
        Assert.True(end.GetCharacterRect(LogicalDirection.Backward).Top > start.GetCharacterRect(LogicalDirection.Forward).Top,
            "窄气泡中的长路径必须跨行，不可成为不可拆分控件");
    });

    [Theory]
    [InlineData("😀")]
    [InlineData("👩🏽‍💻")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("❤️")]
    [InlineData("1️⃣")]
    public void 每个完整字符簇保留可选择文本并有实际彩色像素(string emoji) => StaHost.Run(() =>
    {
        var document = Parse($"甲{emoji}乙");
        var paragraph = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
        var runs = paragraph.Inlines.OfType<Run>().ToArray();
        var emojiRun = Assert.Single(runs.Where(run => run.Text == emoji));
        var brush = Assert.IsType<ImageBrush>(emojiRun.Background);
        var bitmap = Assert.IsAssignableFrom<BitmapSource>(brush.ImageSource);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert.True(Enumerable.Range(0, pixels.Length / 4).Any(i =>
            pixels[i * 4 + 3] > 100 && Math.Max(pixels[i * 4], Math.Max(pixels[i * 4 + 1], pixels[i * 4 + 2])) -
            Math.Min(pixels[i * 4], Math.Min(pixels[i * 4 + 1], pixels[i * 4 + 2])) > 35),
            "字体名称不是彩色渲染证据，位图中必须存在非灰阶像素");
        Assert.Equal($"甲{emoji}乙\r\n", new TextRange(document.ContentStart, document.ContentEnd).Text);
    });

    [Theory]
    [InlineData("普通 © ™ ↔ → ■ ☆ ☺ ♠ 123 # *")]
    [InlineData("文本变体 ☺︎ ♥︎")]
    [InlineData("孤立变体选择符 \uFE0F")]
    public void 普通符号及文本变体不被当作彩色表情(string text) => StaHost.Run(() =>
    {
        var document = Parse(text);
        var paragraph = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
        Assert.DoesNotContain(paragraph.Inlines.OfType<Run>(), run => run.Background is ImageBrush);
        Assert.Equal(text + "\r\n", new TextRange(document.ContentStart, document.ContentEnd).Text);
    });

    [Fact]
    public void 嵌套强调链接与表格同样走排版处理且保留表格对齐() => StaHost.Run(() =>
    {
        var document = Parse("**粗体 😀 `代码`** [链接 👩🏽‍💻](https://example.com)\n\n| 左 | 右 |\n| :--- | ---: |\n| 😀 | `代码` |");
        Assert.Contains(段落(document.Blocks).SelectMany(运行段), run => run.Background is ImageBrush);
        var table = document.Blocks.OfType<Table>().Single();
        Assert.Equal(TextAlignment.Right, table.RowGroups.Last().Rows[0].Cells[1].TextAlignment);
        Assert.Contains(段落(table.RowGroups.Last().Rows[0].Cells[0].Blocks).SelectMany(运行段), run => run.Background is ImageBrush);
        Assert.Single(document.Blocks.OfType<Paragraph>());
    });

    [Fact]
    public void 系统不支持的旗帜仍然整簇可选而不是空白图片() => StaHost.Run(() =>
    {
        var document = Parse("甲🇨🇳乙🇺🇸丙");
        var paragraph = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
        foreach (var flag in new[] { "🇨🇳", "🇺🇸" })
        {
            var run = Assert.Single(paragraph.Inlines.OfType<Run>().Where(run => run.Text == flag));
            Assert.Equal("Segoe UI Emoji", run.FontFamily.Source);
            if (run.Background is not ImageBrush) Assert.NotSame(Brushes.Transparent, run.Foreground);
        }
        Assert.Equal("甲🇨🇳乙🇺🇸丙\r\n", new TextRange(document.ContentStart, document.ContentEnd).Text);
    });

    [Fact(Skip = "当前 Windows Segoe UI Emoji 不提供彩色国家旗帜；需主任务批准补充字体资产，不能用单色回退冒充通过。")]
    public void 国家旗帜具有实际彩色像素() => 每个完整字符簇保留可选择文本并有实际彩色像素("🇨🇳");

    [Theory]
    [InlineData("A️ ←️ ")]
    [InlineData("🂡 ")]
    public void 非表情字符即使带选择符也不触发表情字体(string text) => StaHost.Run(() =>
    {
        var document = Parse(text.TrimEnd());
        var paragraph = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
        Assert.DoesNotContain(paragraph.Inlines.OfType<Run>(), run => run.FontFamily.Source == "Segoe UI Emoji");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 深浅背景下真实控件输出彩色表情且整段可选择(bool dark) => StaHost.Run(() =>
    {
        var document = Parse("中文 `D:\\游戏` 😀👩🏽‍💻❤️1️⃣ English");
        document.PagePadding = new Thickness(0);
        document.Foreground = dark ? Brushes.White : Brushes.Black;
        var viewer = new RichTextBox
        {
            Document = document, Width = 400, Height = 100, Padding = new Thickness(0),
            Background = dark ? Brushes.Black : Brushes.White, BorderThickness = new Thickness(0),
        };
        viewer.Measure(new Size(400, 100));
        viewer.Arrange(new Rect(0, 0, 400, 100));
        viewer.UpdateLayout();
        var image = new RenderTargetBitmap(400, 100, 96, 96, PixelFormats.Pbgra32);
        image.Render(viewer);
        var pixels = new byte[400 * 100 * 4];
        image.CopyPixels(pixels, 400 * 4, 0);
        Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 100 &&
            Math.Max(pixels[i * 4], Math.Max(pixels[i * 4 + 1], pixels[i * 4 + 2])) -
            Math.Min(pixels[i * 4], Math.Min(pixels[i * 4 + 1], pixels[i * 4 + 2])) > 35) > 15,
            "必须验证 WPF 控件的实际彩色输出，而不仅是离屏位图");
        viewer.SelectAll();
        Assert.Equal("中文 D:\\游戏 😀👩🏽‍💻❤️1️⃣ English\r\n", viewer.Selection.Text);
    });

    [Fact]
    public void 已显示代码继承文字色并响应共享主题画刷更新() => StaHost.Run(() =>
    {
        var resources = Application.Current.Resources;
        var oldText = resources["VP.TextPrimaryBrush"];
        var oldCode = resources["VP.SurfaceHoverBrush"];
        var foregroundSource = new SolidColorBrush(Colors.White);
        var backgroundSource = new SolidColorBrush(Colors.DarkSlateGray);
        var foreground = new SolidColorBrush();
        var background = new SolidColorBrush();
        // ThemeManager 以绑定保持画刷可变，真实模拟它的共享画刷更新方式。
        BindingOperations.SetBinding(foreground, SolidColorBrush.ColorProperty,
            new Binding(nameof(SolidColorBrush.Color)) { Source = foregroundSource });
        BindingOperations.SetBinding(background, SolidColorBrush.ColorProperty,
            new Binding(nameof(SolidColorBrush.Color)) { Source = backgroundSource });
        try
        {
            resources["VP.TextPrimaryBrush"] = foreground;
            resources["VP.SurfaceHoverBrush"] = background;
            var document = Parse("正文 `代码` 😀");
            var code = Assert.Single(((Paragraph)document.Blocks.FirstBlock).Inlines.OfType<Span>());
            Assert.Same(foreground, code.Foreground);
            Assert.Same(background, code.Background);
            foregroundSource.Color = Colors.Black;
            backgroundSource.Color = Colors.LightGray;
            Assert.Equal(Colors.Black, ((SolidColorBrush)code.Foreground).Color);
            Assert.Equal(Colors.LightGray, ((SolidColorBrush)code.Background).Color);
        }
        finally
        {
            if (oldText == null) resources.Remove("VP.TextPrimaryBrush"); else resources["VP.TextPrimaryBrush"] = oldText;
            if (oldCode == null) resources.Remove("VP.SurfaceHoverBrush"); else resources["VP.SurfaceHoverBrush"] = oldCode;
        }
    });
    [Fact]
    public void 流式消息的不完整代理项不使整条格式退化() => StaHost.Run(() =>
    {
        var document = Parse("**正文** \uD83D");
        var paragraph = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
        var emphasis = Assert.Single(paragraph.Inlines.OfType<Span>());
        Assert.Equal(FontWeights.Bold, emphasis.FontWeight);
        Assert.Equal("正文 \uD83D\r\n", new TextRange(document.ContentStart, document.ContentEnd).Text);
    });
    private static IEnumerable<Paragraph> 段落(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) yield return paragraph;
            if (block is Section section)
                foreach (var child in 段落(section.Blocks)) yield return child;
            if (block is System.Windows.Documents.List list)
                foreach (var item in list.ListItems)
                    foreach (var child in 段落(item.Blocks)) yield return child;
        }
    }

    private static IEnumerable<Run> 运行段(Paragraph paragraph) => 运行段(paragraph.Inlines);
    private static IEnumerable<Run> 运行段(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            if (inline is Span span)
                foreach (var child in 运行段(span.Inlines)) yield return child;
        }
    }

}
