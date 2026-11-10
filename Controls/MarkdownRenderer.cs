using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OloopApp.Controls;

/// <summary>
/// Renders Markdown as an Avalonia control tree.
///
/// The renderer is deliberately free of theme lookups: it only builds the
/// control structure, the colors and the fonts come from the styles of the
/// app. That way the same code runs in the app and in the headless tests.
///
/// Every inline of a paragraph becomes a small text block, so that bold,
/// italic, code and links keep their own formatting. Lines without any
/// formatting stay a single text block and wrap as usual.
/// </summary>
public static class MarkdownRenderer
{
    private const string Monospace = "Cascadia Code, JetBrains Mono, Consolas, monospace";
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    /// <summary>
    /// Parses the Markdown and returns the control tree of the document.
    /// An empty document gives an empty panel, never null.
    /// </summary>
    public static Control Render(string? markdown)
    {
        var panel = new StackPanel { Spacing = 10 };

        if (string.IsNullOrWhiteSpace(markdown))
            return panel;

        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        foreach (var item in document)
        {
            if (item is Block block)
                AppendBlock(panel, block);
        }

        return panel;
    }

    private static void AppendBlock(Panel parent, Block block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                AppendLines(parent, paragraph.Inline, "BodyLarge", null);
                break;

            case HeadingBlock heading:
                AppendLines(parent, heading.Inline, "TitleMedium", heading.Level switch
                {
                    1 => 20d,
                    2 => 18d,
                    _ => 16d,
                });
                break;

            case ListBlock list:
                parent.Children.Add(CreateList(list));
                break;

            case Table table:
                parent.Children.Add(CreateTable(table));
                break;

            case QuoteBlock quote:
                parent.Children.Add(CreateQuote(quote));
                break;

            case ThematicBreakBlock:
                parent.Children.Add(new Border
                {
                    Classes = { "Divider" },
                    Margin = new Thickness(0, 4, 0, 4),
                });
                break;

            case CodeBlock code:
                parent.Children.Add(CreateCodeBlock(code));
                break;

            case HtmlBlock html:
                parent.Children.Add(new SelectableTextBlock
                {
                    Text = AsText(html.Lines),
                    Classes = { "BodyMedium" },
                    TextWrapping = TextWrapping.Wrap,
                });
                break;

            case ContainerBlock container:
                foreach (var item in container)
                {
                    if (item is Block child)
                        AppendBlock(parent, child);
                }
                break;
        }
    }

    private static Panel CreateTable(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        var grid = new MarkdownTablePanel { Classes = { "MarkdownTable" } };

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var cells = rows[rowIndex].OfType<TableCell>().ToList();

            for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
            {
                var content = new StackPanel { Spacing = 2 };
                foreach (var item in cells[columnIndex])
                {
                    if (item is Block block)
                        AppendBlock(content, block);
                }

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#5B5B66")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 6),
                    Child = content,
                };

                if (rows[rowIndex].IsHeader)
                    SetBold(content);

                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                grid.Children.Add(border);
            }
        }

        return grid;
    }

    private sealed class MarkdownTablePanel : Panel
    {
        private double[] _intrinsicColumnWidths = [];
        private double[] _columnWidths = [];
        private double[] _rowHeights = [];

        protected override Size MeasureOverride(Size availableSize)
        {
            var columnCount = Children.Count == 0 ? 0 : Children.Max(child => Grid.GetColumn(child)) + 1;
            var rowCount = Children.Count == 0 ? 0 : Children.Max(child => Grid.GetRow(child)) + 1;
            if (columnCount == 0 || rowCount == 0)
                return default;

            _intrinsicColumnWidths = new double[columnCount];
            foreach (var child in Children)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var column = Grid.GetColumn(child);
                _intrinsicColumnWidths[column] = Math.Max(_intrinsicColumnWidths[column], child.DesiredSize.Width);
            }

            var availableWidth = double.IsFinite(availableSize.Width)
                ? Math.Max(0, availableSize.Width)
                : _intrinsicColumnWidths.Sum();
            _columnWidths = DistributeWidth(availableWidth, _intrinsicColumnWidths);
            _rowHeights = new double[rowCount];

            foreach (var child in Children)
            {
                var row = Grid.GetRow(child);
                var column = Grid.GetColumn(child);
                child.Measure(new Size(_columnWidths[column], availableSize.Height));
                _rowHeights[row] = Math.Max(_rowHeights[row], child.DesiredSize.Height);
            }

            return new Size(availableWidth, _rowHeights.Sum());
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_columnWidths.Length == 0 || _rowHeights.Length == 0)
                return finalSize;

            _columnWidths = DistributeWidth(finalSize.Width, _intrinsicColumnWidths);
            var columnOffsets = new double[_columnWidths.Length];
            var rowOffsets = new double[_rowHeights.Length];

            for (var column = 1; column < columnOffsets.Length; column++)
                columnOffsets[column] = columnOffsets[column - 1] + _columnWidths[column - 1];

            for (var row = 1; row < rowOffsets.Length; row++)
                rowOffsets[row] = rowOffsets[row - 1] + _rowHeights[row - 1];

            foreach (var child in Children)
            {
                var row = Grid.GetRow(child);
                var column = Grid.GetColumn(child);
                child.Arrange(new Rect(
                    columnOffsets[column],
                    rowOffsets[row],
                    _columnWidths[column],
                    _rowHeights[row]));
            }

            return finalSize;
        }

        private static double[] DistributeWidth(double availableWidth, double[] intrinsicWidths)
        {
            var widths = new double[intrinsicWidths.Length];
            var remainingColumns = Enumerable.Range(0, intrinsicWidths.Length).ToList();
            var remainingWidth = availableWidth;

            while (remainingColumns.Count > 0)
            {
                var equalShare = remainingWidth / remainingColumns.Count;
                var compactColumns = remainingColumns
                    .Where(column => intrinsicWidths[column] <= equalShare)
                    .ToArray();

                if (compactColumns.Length == 0)
                {
                    foreach (var column in remainingColumns)
                        widths[column] = equalShare;
                    break;
                }

                foreach (var column in compactColumns)
                {
                    widths[column] = intrinsicWidths[column];
                    remainingWidth -= widths[column];
                    remainingColumns.Remove(column);
                }
            }

            return widths;
        }
    }

    private static void SetBold(Control control)
    {
        switch (control)
        {
            case TextBlock text:
                text.FontWeight = FontWeight.SemiBold;
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                    SetBold(child);
                break;
            case Border { Child: { } child }:
                SetBold(child);
                break;
        }
    }

    private static void AppendLines(Panel parent, ContainerInline? inline, string styleClass, double? fontSize)
    {
        if (inline is null)
        {
            parent.Children.Add(new TextBlock { Classes = { styleClass } });
            return;
        }

        foreach (var line in CollectLines(inline))
            parent.Children.Add(CreateLine(line, styleClass, fontSize));
    }

    private static Control CreateLine(IReadOnlyList<Segment> segments, string styleClass, double? fontSize)
    {
        var merged = Merge(segments);

        if (merged.Count == 1)
            return CreateSegment(merged[0], styleClass, fontSize);

        var panel = new WrapPanel { Orientation = Orientation.Horizontal };

        foreach (var segment in merged)
            panel.Children.Add(CreateSegment(segment, styleClass, fontSize));

        return panel;
    }

    private static SelectableTextBlock CreateSegment(Segment segment, string styleClass, double? fontSize)
    {
        var text = new SelectableTextBlock
        {
            Text = segment.Text,
            Classes = { styleClass },
            TextWrapping = TextWrapping.Wrap,
        };

        if (fontSize is { } size)
            text.FontSize = size;

        if (segment.Bold)
            text.FontWeight = FontWeight.SemiBold;

        if (segment.Italic)
            text.FontStyle = FontStyle.Italic;

        if (segment.Code)
            text.FontFamily = new FontFamily(Monospace);

        if (segment.Link)
            text.TextDecorations = TextDecorations.Underline;

        return text;
    }

    private static Control CreateList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 4 };
        var number = list.IsOrdered ? ParseOrderedStart(list.OrderedStart) : 0;

        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem)
                continue;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
            };

            row.Children.Add(new SelectableTextBlock
            {
                Text = list.IsOrdered ? $"{number}." : "•",
                Classes = { "BodyLarge" },
                MinWidth = 18,
                VerticalAlignment = VerticalAlignment.Top,
            });

            var content = new StackPanel { Spacing = 4 };

            foreach (var child in listItem)
            {
                if (child is Block childBlock)
                    AppendBlock(content, childBlock);
            }

            row.Children.Add(content);
            panel.Children.Add(row);

            number++;
        }

        var copyButton = new Button
        {
            Classes = { "Text" },
            Content = "Copy list",
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var copyText = GetListText(list);
        copyButton.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(copyButton);
            if (top?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(copyText);
        };

        panel.Children.Add(copyButton);
        return panel;
    }

    private static string GetListText(ListBlock list, int depth = 0)
    {
        var lines = new List<string>();
        var number = list.IsOrdered ? ParseOrderedStart(list.OrderedStart) : 0;

        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem)
                continue;

            var marker = new string(' ', depth * 2) + (list.IsOrdered ? $"{number}. " : "• ");
            var hasContent = false;

            foreach (var child in listItem)
            {
                if (child is not Block childBlock)
                    continue;

                if (childBlock is ListBlock nestedList)
                {
                    if (!hasContent)
                        lines.Add(marker.TrimEnd());

                    lines.AddRange(GetListText(nestedList, depth + 1).Split('\n'));
                    hasContent = true;
                    continue;
                }

                var blockLines = GetListItemText(childBlock);
                if (blockLines.Count == 0)
                    continue;

                if (!hasContent)
                {
                    lines.Add(marker + blockLines[0]);
                    hasContent = true;
                    blockLines.RemoveAt(0);
                }

                var continuationIndent = new string(' ', depth * 2 + 2);
                lines.AddRange(blockLines.Select(line => continuationIndent + line));
            }

            if (!hasContent)
                lines.Add(marker.TrimEnd());

            number++;
        }

        return string.Join("\n", lines);
    }

    private static List<string> GetListItemText(Block block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                return GetInlineText(paragraph.Inline);

            case HeadingBlock heading:
                return GetInlineText(heading.Inline);

            case CodeBlock code:
                return AsText(code.Lines).Split('\n').Select(line => line.TrimEnd('\r')).ToList();

            case HtmlBlock html:
                return AsText(html.Lines).Split('\n').Select(line => line.TrimEnd('\r')).ToList();

            case QuoteBlock quote:
                var lines = new List<string>();
                foreach (var child in quote)
                {
                    if (child is Block childBlock)
                        lines.AddRange(GetListItemText(childBlock));
                }
                return lines;

            default:
                return new List<string>();
        }
    }

    private static List<string> GetInlineText(ContainerInline? inline)
    {
        if (inline is null)
            return new List<string>();

        return CollectLines(inline)
            .Select(line => string.Concat(Merge(line).Select(segment => segment.Text)))
            .ToList();
    }

    private static Control CreateQuote(QuoteBlock quote)
    {
        var content = new StackPanel { Spacing = 8 };

        foreach (var item in quote)
        {
            if (item is Block child)
                AppendBlock(content, child);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#5B5B66")),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0),
            Child = content,
        };
    }

    private static CodeBlockView CreateCodeBlock(CodeBlock code)
    {
        var view = new CodeBlockView
        {
            Code = AsText(code.Lines),
        };

        if (code is FencedCodeBlock fenced)
        {
            var language = AsText(fenced.Info).Trim();

            if (language.Length > 0)
                view.Language = language;
        }

        return view;
    }

    /// <summary>
    /// Splits the inline tree of a block into its lines. A hard line break
    /// ends the current line.
    /// </summary>
    private static List<List<Segment>> CollectLines(ContainerInline container)
    {
        var lines = new List<List<Segment>>();
        var current = new List<Segment>();

        var child = container.FirstChild;

        while (child is not null)
        {
            if (child is LineBreakInline)
            {
                lines.Add(current);
                current = new List<Segment>();
            }
            else
            {
                Collect(child, current, false, false, false, false);
            }

            child = child.NextSibling;
        }

        lines.Add(current);

        return lines;
    }

    private static void Collect(
        Inline inline,
        List<Segment> segments,
        bool bold,
        bool italic,
        bool code,
        bool link)
    {
        switch (inline)
        {
            case HtmlEntityInline entity:
                segments.Add(new Segment(AsText(entity.Transcoded), bold, italic, code, link));
                break;

            case LiteralInline literal:
                segments.Add(new Segment(AsText(literal.Content), bold, italic, code, link));
                break;

            case CodeInline codeInline:
                segments.Add(new Segment(AsText(codeInline.Content), bold, italic, true, link));
                break;

            case AutolinkInline autolink:
                segments.Add(new Segment(AsText(autolink.Url), bold, italic, code, true));
                break;

            case EmphasisInline emphasis:
            {
                var strong = emphasis.DelimiterCount >= 2;
                var child = emphasis.FirstChild;

                while (child is not null)
                {
                    Collect(child, segments, bold || strong, italic || !strong, code, link);
                    child = child.NextSibling;
                }

                break;
            }

            case LinkInline linkInline:
            {
                var before = segments.Count;
                var child = linkInline.FirstChild;

                while (child is not null)
                {
                    Collect(child, segments, bold, italic, code, true);
                    child = child.NextSibling;
                }

                if (segments.Count == before)
                    segments.Add(new Segment(AsText(linkInline.Url), bold, italic, code, true));

                break;
            }

            case ContainerInline container:
            {
                var child = container.FirstChild;

                while (child is not null)
                {
                    Collect(child, segments, bold, italic, code, link);
                    child = child.NextSibling;
                }

                break;
            }
        }
    }

    /// <summary>
    /// Joins the segments that share their formatting, so that a plain
    /// paragraph stays one text block.
    /// </summary>
    private static List<Segment> Merge(IReadOnlyList<Segment> segments)
    {
        var merged = new List<Segment>();

        foreach (var segment in segments)
        {
            if (segment.Text.Length == 0)
                continue;

            if (merged.Count > 0)
            {
                var last = merged[^1];

                if (last.Bold == segment.Bold
                    && last.Italic == segment.Italic
                    && last.Code == segment.Code
                    && last.Link == segment.Link)
                {
                    merged[^1] = last with { Text = last.Text + segment.Text };
                    continue;
                }
            }

            merged.Add(segment);
        }

        return merged;
    }

    /// <summary>
    /// The text of a value of the Markdown tree. The tree stores its text as
    /// <see cref="StringSlice"/>, but a plain string has to work as well.
    /// </summary>
    private static string AsText(object? value) => value switch
    {
        null => "",
        string text => text,
        StringSlice slice => slice.ToString() ?? "",
        IEnumerable<StringSlice> slices => string.Join("\n", slices.Select(s => s.ToString() ?? "")),
        _ => value.ToString() ?? "",
    };

    /// <summary>One piece of a line: the text and how it is formatted.</summary>
    private readonly record struct Segment(string Text, bool Bold, bool Italic, bool Code, bool Link);

    /// <summary>
    /// The start number of an ordered list. Markdig keeps it as a string, so
    /// an unparsable value falls back to 1.
    /// </summary>
    private static int ParseOrderedStart(string? raw)
    {
        return int.TryParse(raw, out var value) ? value : 1;
    }
}
