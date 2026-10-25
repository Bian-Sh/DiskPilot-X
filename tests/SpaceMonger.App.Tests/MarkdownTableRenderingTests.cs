using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SpaceMonger.App.Converters;

namespace SpaceMonger.App.Tests;

public sealed class MarkdownTableRenderingTests
{
    [Fact]
    public void PipeTableRendersAsNativeTable()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("| 文件 | 大小 | 说明 |\n| --- | ---: | :---: |\n| a.exe | 10 MB | 安全 |");

            var table = Assert.IsType<Table>(Assert.Single(document.Blocks.OfType<Table>()));
            Assert.Equal(3, table.Columns.Count);
            Assert.Equal(2, table.RowGroups.Count);

            var header = table.RowGroups[0].Rows[0];
            Assert.NotNull(header.Cells[0].Background);
            Assert.Equal(FontWeights.SemiBold, ((Paragraph)header.Cells[0].Blocks.FirstBlock).FontWeight);
            Assert.Equal("文件", GetCellText(header.Cells[0]));

            var body = table.RowGroups[1].Rows[0];
            Assert.Equal(TextAlignment.Left, body.Cells[0].TextAlignment);
            Assert.Equal(TextAlignment.Right, body.Cells[1].TextAlignment);
            Assert.Equal(TextAlignment.Center, body.Cells[2].TextAlignment);
            Assert.Equal("10 MB", GetCellText(body.Cells[1]));
        });
    }

    [Fact]
    public void TableCellKeepsInlineCodeAndEmphasis()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("| A |\n| --- |\n| `x.exe` 与 **加粗** |");
            var paragraph = (Paragraph)document.Blocks.OfType<Table>().Single().RowGroups[1].Rows[0].Cells[0].Blocks.FirstBlock;
            Assert.NotEmpty(paragraph.Inlines.OfType<InlineUIContainer>());
            Assert.Contains(paragraph.Inlines.OfType<Span>(), s => s.FontWeight == FontWeights.Bold);
        });
    }

    [Fact]
    public void TableMeasuresAndArrangesWithoutThrowing()
    {
        RunOnSta(() =>
        {
            var document = ConvertToDocument("前文段落\n\n| 名称 | 大小 |\n| --- | --- |\n| a.exe | 10 MB |\n\n结尾段落");
            var viewer = new FlowDocumentScrollViewer { Document = document, Width = 480 };
            viewer.Measure(new Size(480, double.PositiveInfinity));
            viewer.Arrange(new Rect(0, 0, 480, viewer.DesiredSize.Height));
            viewer.UpdateLayout();
            Assert.True(viewer.DesiredSize.Height > 0);
        });
    }
    private static FlowDocument ConvertToDocument(string markdown)
        => (FlowDocument)new MarkdownToFlowDocumentConverter().Convert(markdown, typeof(FlowDocument), null!, CultureInfo.InvariantCulture);

    private static string GetCellText(TableCell cell)
        => string.Concat(cell.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).Select(r => r.Text));

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