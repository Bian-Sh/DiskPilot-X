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

    private static FlowDocument ParseMarkdown(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = 13,
            Foreground = (Brush)Application.Current.FindResource("VP.TextPrimaryBrush"),
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
            Background = (Brush)Application.Current.FindResource("VP.BorderSubtleBrush")
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
                    linkSpan.Foreground = (Brush)Application.Current.FindResource("VP.AccentBrush");
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
                    linkSpan.Foreground = (Brush)Application.Current.FindResource("VP.AccentBrush");
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
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = new TextBlock
            {
                Text = code.Content,
                FontFamily = new FontFamily("Cascadia Code, SF Mono, Menlo, Consolas, monospace"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 192, 123)),
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
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = (Brush)Application.Current.FindResource("VP.BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
        };

        var textBlock = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Cascadia Code, SF Mono, Menlo, Consolas, monospace"),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(169, 220, 118)),
            TextWrapping = TextWrapping.Wrap,
        };

        container.Child = textBlock;
        section.Blocks.Add(new BlockUIContainer(container));

        return section;
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
                    Foreground = (Brush)Application.Current.FindResource("VP.TextSecondaryBrush"),
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
            BorderBrush = (Brush)Application.Current.FindResource("VP.AccentBrush"),
        };

        foreach (var block in quote)
        {
            if (block is ParagraphBlock para)
            {
                var paragraph = new Paragraph
        {
            Foreground = (Brush)Application.Current.FindResource("VP.TextSecondaryBrush"),
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
