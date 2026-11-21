using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;

namespace Notifs.App;

/// <summary>Markdown AST to native text. No browser, resource loader, HTML, or clickable remote links.</summary>
public sealed class MarkdownView : FlowDocumentScrollViewer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(string), typeof(MarkdownView),
        new PropertyMetadata("", (sender, _) => ((MarkdownView)sender).Render()));
    public string Source { get => (string)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public MarkdownView()
    {
        FontSize = 16;
        Foreground = (Brush)Application.Current.FindResource("Ink");
        IsToolBarVisible = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        FocusVisualStyle = (Style)Application.Current.FindResource("FocusOutline");
        Background = Brushes.Transparent;
    }
    private void Render()
    {
        var document = new FlowDocument { FontFamily = (FontFamily)FindResource("BodyFont"), FontSize = FontSize, LineHeight = FontSize * 1.8,
            PagePadding = new Thickness(0), Foreground = Foreground };
        foreach (var block in Markdown.Parse(Source ?? "", Pipeline)) document.Blocks.Add(ConvertBlock(block));
        Document = document;
    }
    private WpfBlock ConvertBlock(MdBlock block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var h = ParagraphFrom(heading.Inline);
                h.FontSize = FontSize * (heading.Level switch { 1 => 1.625, 2 => 1.375, _ => 16.0 / 13 });
                h.FontWeight = FontWeights.SemiBold;
                h.Foreground = (Brush)FindResource("Ink");
                h.Margin = new Thickness(0, 0, 0, 10);
                return h;
            case ParagraphBlock paragraph:
                return ParagraphFrom(paragraph.Inline);
            case CodeBlock code:
                return new Paragraph(new Run(code.Lines.ToString())) { FontFamily = (FontFamily)FindResource("CodeFont"),
                    FontSize = 14, LineHeight = 22, Background = (Brush)FindResource("Background"), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 16) };
            case ListBlock list:
                var result = new WpfList { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Padding = new Thickness(24, 0, 0, 0), Margin = new Thickness(0, 4, 0, 12) };
                if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start) && start > 0) result.StartIndex = start;
                foreach (ListItemBlock item in list)
                {
                    var entry = new ListItem();
                    foreach (var child in item)
                    {
                        var converted = ConvertBlock(child);
                        if (!list.IsLoose && converted is Paragraph) converted.Margin = new Thickness(0);
                        entry.Blocks.Add(converted);
                    }
                    result.ListItems.Add(entry);
                }
                return result;
            case QuoteBlock quote:
                var section = new Section { BorderBrush = (Brush)FindResource("Primary"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(16, 0, 0, 0) };
                foreach (var child in quote) section.Blocks.Add(ConvertBlock(child));
                return section;
            case ThematicBreakBlock:
                return new Paragraph { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 12, 0, 12) };
            default:
                return new Paragraph(new Run(block is LeafBlock leaf ? leaf.Lines.ToString() : ""));
        }
    }
    private static Paragraph ParagraphFrom(ContainerInline? inline)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
        if (inline is not null) AddInlines(paragraph.Inlines, inline);
        return paragraph;
    }
    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var child in container)
        {
            switch (child)
            {
                case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code: target.Add(new Run(code.Content) { FontFamily = (FontFamily)Application.Current.FindResource("CodeFont"), Background = (Brush)Application.Current.FindResource("Background") }); break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    AddInlines(span.Inlines, emphasis); target.Add(span); break;
                case LinkInline link:
                    // URLs and image alt text are text only: viewing never causes network/file IO.
                    var label = new Span(); AddInlines(label.Inlines, link); target.Add(label);
                    if (link.IsImage) target.Add(new Run(" [изображение]"));
                    else if (!string.IsNullOrEmpty(link.Url)) target.Add(new Run($" ({link.Url})"));
                    break;
                case ContainerInline nested: AddInlines(target, nested); break;
            }
        }
    }
}
