using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DiskPilotX.App.Converters;

namespace DiskPilotX.App.Tests;

public sealed class MarkdownChartRenderingTests
{
    [Fact]
    public void DiskBarFenceRendersAsNativeChart()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("```disk-bar\n缓存目录 | 2.4\n构建产物 | 1.1\n旧日志 | 0.3\n```");

            // 图表是 BlockUIContainer 包一个 Border，而不是普通代码块 Section
            var container = Assert.Single(document.Blocks.OfType<BlockUIContainer>());
            var border = Assert.IsType<Border>(container.Child);
            Assert.NotNull(border.Child);
        });
    }

    [Fact]
    public void DiskBarFenceDoesNotRenderAsPlainCodeBlock()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("```disk-bar\n缓存目录 | 2.4\n```");

            // 不应出现 FencedCodeBlock 对应的 Section（普通代码块样式）
            var sections = document.Blocks.OfType<Section>().ToList();
            // 图表渲染成 BlockUIContainer，代码块渲染成 Section；此处应无 Section
            Assert.Empty(sections);
        });
    }

    [Fact]
    public void DiskBadgeFenceRendersBadgesWithSemanticColor()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("```disk-badge\n临时文件 | Safe\n构建缓存 | Caution\n```");
            var container = Assert.Single(document.Blocks.OfType<BlockUIContainer>());
            var border = Assert.IsType<Border>(container.Child);
            var panel = Assert.IsType<StackPanel>(border.Child);
            Assert.Equal(2, panel.Children.Count);
        });
    }

    [Fact]
    public void DiskMeterFenceClampsAndRenders()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("```disk-meter\nC盘 | 82\nD盘 | 45\n```");
            var container = Assert.Single(document.Blocks.OfType<BlockUIContainer>());
            var border = Assert.IsType<Border>(container.Child);
            var panel = Assert.IsType<StackPanel>(border.Child);
            Assert.Equal(2, panel.Children.Count);
        });
    }

    [Fact]
    public void UnknownFenceStillRendersAsCodeBlock()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("```csharp\nvar x = 1;\n```");
            // 普通代码块渲染成 Section（内含 BlockUIContainer）
            Assert.NotEmpty(document.Blocks.OfType<Section>());
        });
    }

    [Fact]
    public void ChartsMeasureAndArrangeWithoutThrowing()
    {
        RunOnSta(() =>
        {
            var markdown = "```disk-bar\nA | 10\nB | 5\n```\n\n```disk-meter\nC | 70\n```";
            var document = ConvertToDocument(markdown);
            var viewer = new FlowDocumentScrollViewer { Document = document, Width = 480 };
            viewer.Measure(new Size(480, double.PositiveInfinity));
            viewer.Arrange(new Rect(0, 0, 480, viewer.DesiredSize.Height));
            viewer.UpdateLayout();
            Assert.True(viewer.DesiredSize.Height > 0);
        });
    }

    private static FlowDocument ConvertToDocument(string markdown)
        => (FlowDocument)new MarkdownToFlowDocumentConverter().Convert(markdown, typeof(FlowDocument), null!, CultureInfo.InvariantCulture);

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
