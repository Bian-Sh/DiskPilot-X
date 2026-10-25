using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;
using SpaceMonger.App.Helpers;

namespace SpaceMonger.App.Converters;

public class MarkdownToFlowDocumentConverter : IValueConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string markdown || string.IsNullOrWhiteSpace(markdown))
            return new FlowDocument();

        return ParseMarkdown(markdown);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Brush> FallbackBrushes = new();

    /// <summary>查找主题画刷；Application 未初始化或资源缺失时回退到固定色，避免整条消息退化为纯文本。</summary>
    private static Brush FindBrush(string key, Color fallback)
        => Application.Current?.TryFindResource(key) as Brush
           ?? FallbackBrushes.GetOrAdd(key, _ =>
           {
               var brush = new SolidColorBrush(fallback);
               brush.Freeze();
               return brush;
           });
    private static FlowDocument ParseMarkdown(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = 13,
            Foreground = FindBrush("VP.TextPrimaryBrush", Color.FromRgb(0xE8, 0xE8, 0xE8)),
        };

        try
        {
            var md = Markdig.Markdown.Parse(markdown, Pipeline);
            var currentParagraph = new Paragraph();

            foreach (var block in md)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        document.Blocks.Add(CreateHeading(heading));
                        break;

                    case ParagraphBlock para:
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        currentParagraph = CreateParagraph(para);
                        document.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                        break;

                    case ListBlock list:
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        document.Blocks.Add(CreateList(list));
                        break;

                    case FencedCodeBlock codeBlock:
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        document.Blocks.Add(CreateCodeBlock(codeBlock));
                        break;

                    case ThematicBreakBlock:
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        document.Blocks.Add(new BlockUIContainer(new Separator
        {
            Margin = new Thickness(0, 8, 0, 8),
            Background = FindBrush("VP.BorderSubtleBrush", Color.FromRgb(0x44, 0x44, 0x44))
        }));
                        break;

                    case QuoteBlock quote:
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        document.Blocks.Add(CreateQuote(quote));
                        break;

                    case MdTable table:
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        document.Blocks.Add(CreateTable(table));
                        break;
                }
            }

            if (currentParagraph.Inlines.Count > 0)
            {
                document.Blocks.Add(currentParagraph);
            }
        }
        catch
        {
            // Fallback to plain text
            document.Blocks.Add(new Paragraph(new Run(markdown)));
        }

        return document;
    }

    private static Paragraph CreateHeading(HeadingBlock heading)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 12, 0, 4),
        };

        var inline = new Span();
        AddInlines(inline, heading.Inline);

        switch (heading.Level)
        {
            case 1:
                paragraph.FontSize = 20;
                paragraph.FontWeight = FontWeights.Bold;
                break;
            case 2:
                paragraph.FontSize = 17;
                paragraph.FontWeight = FontWeights.Bold;
                break;
            case 3:
                paragraph.FontSize = 15;
                paragraph.FontWeight = FontWeights.SemiBold;
                break;
            case 4:
                paragraph.FontSize = 14;
                paragraph.FontWeight = FontWeights.SemiBold;
                break;
            default:
                paragraph.FontSize = 13;
                paragraph.FontWeight = FontWeights.SemiBold;
                break;
        }

        paragraph.Inlines.Add(inline);
        return paragraph;
    }

    private static Paragraph CreateParagraph(ParagraphBlock paraBlock)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 4),
        };

        if (paraBlock.Inline != null)
        {
            AddInlinesToParagraph(paragraph, paraBlock.Inline);
        }

        return paragraph;
    }

    private static void AddInlinesToParagraph(Paragraph paragraph, ContainerInline container)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    paragraph.Inlines.Add(new Run(literal.Content.ToString()));
                    break;

                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterCount == 2)
                        span.FontWeight = FontWeights.Bold;
                    else if (emphasis.DelimiterCount == 1)
                        span.FontStyle = FontStyles.Italic;

                    AddInlines(span, emphasis);
                    paragraph.Inlines.Add(span);
                    break;

                case CodeInline code:
                    paragraph.Inlines.Add(CreateCodeInline(code));
                    break;

                case LinkInline link:
                    var linkSpan = new Span();
                    linkSpan.Foreground = FindBrush("VP.AccentBrush", Color.FromRgb(0x4D, 0x9F, 0xFF));
                    linkSpan.TextDecorations = TextDecorations.Underline;
                    linkSpan.Cursor = System.Windows.Input.Cursors.Hand;
                    AddInlines(linkSpan, link);
                    paragraph.Inlines.Add(linkSpan);
                    break;

                case LineBreakInline:
                    paragraph.Inlines.Add(new LineBreak());
                    break;

                case ContainerInline containerInline:
                    AddInlinesToParagraph(paragraph, containerInline);
                    break;
            }
        }
    }

    private static void AddInlines(Span parent, ContainerInline container)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    parent.Inlines.Add(new Run(literal.Content.ToString()));
                    break;

                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterCount == 2)
                        span.FontWeight = FontWeights.Bold;
                    else if (emphasis.DelimiterCount == 1)
                        span.FontStyle = FontStyles.Italic;

                    AddInlines(span, emphasis);
                    parent.Inlines.Add(span);
                    break;

                case CodeInline code:
                    parent.Inlines.Add(CreateCodeInline(code));
                    break;

                case LinkInline link:
                    var linkSpan = new Span();
                    linkSpan.Foreground = FindBrush("VP.AccentBrush", Color.FromRgb(0x4D, 0x9F, 0xFF));
                    linkSpan.TextDecorations = TextDecorations.Underline;
                    linkSpan.Cursor = System.Windows.Input.Cursors.Hand;
                    AddInlines(linkSpan, link);
                    parent.Inlines.Add(linkSpan);
                    break;

                case LineBreakInline:
                    parent.Inlines.Add(new LineBreak());
                    break;

                case ContainerInline containerInline:
                    AddInlines(parent, containerInline);
                    break;
            }
        }
    }

    private static InlineUIContainer CreateCodeInline(CodeInline code)
    {
        var border = new Border
        {
            Background = FindBrush("VP.SurfaceHoverBrush", Color.FromRgb(0x2A, 0x2A, 0x2A)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = new TextBlock
            {
                Text = code.Content,
                FontFamily = new FontFamily("Cascadia Code, SF Mono, Menlo, Consolas, monospace"),
                FontSize = 12,
                Foreground = FindBrush("VP.TextPrimaryBrush", Color.FromRgb(0xE8, 0xE8, 0xE8)),
            }
        };

        return new InlineUIContainer(border);
    }

    private static Section CreateCodeBlock(FencedCodeBlock codeBlock)
    {
        var code = string.Join(Environment.NewLine, codeBlock.Lines);

        var section = new Section
        {
            Margin = new Thickness(0, 8, 0, 8),
        };

        var container = new Border
        {
            Background = FindBrush("VP.SurfaceBrush", Color.FromRgb(0x1E, 0x1E, 0x1E)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = FindBrush("VP.BorderSubtleBrush", Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
        };

        var textBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Cascadia Code, SF Mono, Menlo, Consolas, monospace"),
            FontSize = 12.5,
            Foreground = FindBrush("VP.TextPrimaryBrush", Color.FromRgb(0xE8, 0xE8, 0xE8)),
            TextWrapping = TextWrapping.Wrap,
        };

        container.Child = textBlock;
        section.Blocks.Add(new BlockUIContainer(container));

        return section;
    }

    /// <summary>渲染 Markdown 管道表格为 FlowDocument 原生表格。</summary>
    private static System.Windows.Documents.Table CreateTable(MdTable mdTable)
    {
        var borderBrush = FindBrush("VP.BorderSubtleBrush", Color.FromRgb(0x44, 0x44, 0x44));
        var headerBrush = FindBrush("VP.SurfaceHoverBrush", Color.FromRgb(0x2A, 0x2A, 0x2A));

        // Markdig 会为行尾竖线附加空列定义，列数以最宽数据行为准，兜底取分隔行声明
        var columnCount = mdTable.OfType<MdTableRow>().Select(r => r.Count).DefaultIfEmpty(mdTable.ColumnDefinitions.Count).Max();

        var table = new System.Windows.Documents.Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 10),
        };
        for (var i = 0; i < columnCount; i++) table.Columns.Add(new TableColumn());

        var headerGroup = new TableRowGroup();
        var bodyGroup = new TableRowGroup();

        foreach (var rowBlock in mdTable)
        {
            if (rowBlock is not MdTableRow mdRow) continue;

            var row = new TableRow();
            for (var i = 0; i < mdRow.Count; i++)
            {
                if (mdRow[i] is not MdTableCell mdCell) continue;

                var cell = new TableCell
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4, 8, 4),
                };

                if (mdRow.IsHeader) cell.Background = headerBrush;

                var alignment = i < mdTable.ColumnDefinitions.Count ? mdTable.ColumnDefinitions[i].Alignment : null;
                cell.TextAlignment = alignment switch
                {
                    MdTableColumnAlign.Center => TextAlignment.Center,
                    MdTableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };

                var hasContent = false;
                foreach (var cellBlock in mdCell)
                {
                    if (cellBlock is not ParagraphBlock para) continue;

                    // TableCell 默认自带一个空段落，填入内容前先移除
                    if (!hasContent && cell.Blocks.FirstBlock is Paragraph empty && empty.Inlines.Count == 0)
                        cell.Blocks.Remove(empty);

                    var paragraph = new Paragraph { Margin = new Thickness(0) };
                    if (mdRow.IsHeader) paragraph.FontWeight = FontWeights.SemiBold;
                    if (para.Inline != null) AddInlinesToParagraph(paragraph, para.Inline);
                    cell.Blocks.Add(paragraph);
                    hasContent = true;
                }

                row.Cells.Add(cell);
            }

            (mdRow.IsHeader ? headerGroup : bodyGroup).Rows.Add(row);
        }

        if (headerGroup.Rows.Count > 0) table.RowGroups.Add(headerGroup);
        if (bodyGroup.Rows.Count > 0) table.RowGroups.Add(bodyGroup);
        return table;
    }
    private static Section CreateList(ListBlock list)
    {
        var section = new Section
        {
            Margin = new Thickness(16, 4, 0, 4),
        };

        int index = 0;
        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 2, 0, 2),
                };

                var bullet = list.IsOrdered ? $"{++index}. " : "• ";
                paragraph.Inlines.Add(new Run(bullet)
                {
                    Foreground = FindBrush("VP.TextSecondaryBrush", Color.FromRgb(0xA8, 0xA8, 0xA8)),
                });

                foreach (var subBlock in listItem)
                {
                    if (subBlock is ParagraphBlock para && para.Inline != null)
                    {
                        AddInlinesToParagraph(paragraph, para.Inline);
                    }
                }

                section.Blocks.Add(paragraph);
            }
        }

        return section;
    }

    private static Section CreateQuote(QuoteBlock quote)
    {
        var section = new Section
        {
            Margin = new Thickness(12, 8, 0, 8),
            Padding = new Thickness(12, 0, 0, 0),
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = FindBrush("VP.AccentBrush", Color.FromRgb(0x4D, 0x9F, 0xFF)),
        };

        foreach (var block in quote)
        {
            if (block is ParagraphBlock para)
            {
                var paragraph = new Paragraph
        {
            Foreground = FindBrush("VP.TextSecondaryBrush", Color.FromRgb(0xA8, 0xA8, 0xA8)),
            FontStyle = FontStyles.Italic,
        };

                if (para.Inline != null)
                {
                    AddInlinesToParagraph(paragraph, para.Inline);
                }

                section.Blocks.Add(paragraph);
            }
        }

        return section;
    }
}
