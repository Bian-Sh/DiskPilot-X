using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;

namespace DiskPilotX.App.Converters;

/// <summary>只收紧更新日志的排版，不改变聊天文档的默认样式。</summary>
public sealed class ReleaseNotesMarkdownConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var document = (FlowDocument)new MarkdownToFlowDocumentConverter().Convert(value, targetType, parameter, culture);
        document.PagePadding = new Thickness(0);
        document.FontSize = 13;
        document.LineHeight = 19;
        document.ColumnWidth = double.PositiveInfinity;
        Compact(document.Blocks);
        return document;
    }

    private static void Compact(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            block.LineHeight = 19;
            switch (block)
            {
                case Paragraph paragraph:
                    var heading = paragraph.ReadLocalValue(TextElement.FontSizeProperty) is double size && size >= 14;
                    if (heading)
                    {
                        paragraph.FontSize = paragraph.FontSize switch { >= 20 => 18, >= 17 => 16, var current => current };
                        paragraph.Margin = new Thickness(0, 6, 0, 2);
                    }
                    else paragraph.Margin = new Thickness(0, 1, 0, 1);
                    break;
                case System.Windows.Documents.List list:
                    list.Margin = new Thickness(0, 2, 0, 2);
                    list.Padding = new Thickness(16, 0, 0, 0);
                    foreach (var item in list.ListItems) { item.Margin = new Thickness(0); Compact(item.Blocks); }
                    break;
                case Section section:
                    section.Margin = new Thickness(0, 3, 0, 3);
                    Compact(section.Blocks);
                    break;
                case Table table:
                    table.Margin = new Thickness(0, 3, 0, 3);
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells) { cell.Padding = new Thickness(5, 2, 5, 2); Compact(cell.Blocks); }
                    break;
            }
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
