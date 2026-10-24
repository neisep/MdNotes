using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

namespace MdNotes.Views.Markdown;

/// <summary>Turns Markdig block nodes into Avalonia controls.</summary>
internal static class BlockRenderer
{
    public static readonly FontFamily Mono = new("Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,monospace");

    public static Control RenderDocument(MarkdownDocument document, RenderContext ctx) => RenderBlocks(document, ctx, spacing: 16);

    private static StackPanel RenderBlocks(ContainerBlock container, RenderContext ctx, double spacing)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var block in container)
            if (RenderBlock(block, ctx) is { } control)
                panel.Children.Add(control);
        return panel;
    }

    private static Control? RenderBlock(Block block, RenderContext ctx) => block switch
    {
        HeadingBlock heading => Heading(heading, ctx),
        ParagraphBlock paragraph => InlineRenderer.Paragraph(paragraph.Inline, ctx),
        ListBlock list => List(list, ctx),
        QuoteBlock quote => Quote(quote, ctx),
        FencedCodeBlock fenced when IsMermaid(fenced) => Diagram(fenced, ctx),
        // Must precede CodeBlock: YamlFrontMatterBlock derives from it, and frontmatter is
        // metadata about the note rather than part of it, so it renders as nothing.
        YamlFrontMatterBlock => null,
        CodeBlock code => Code(code, ctx.Look),
        ThematicBreakBlock => Rule(ctx.Look),
        Table table => TableGrid(table, ctx),
        HtmlBlock html => new TextBlock { Text = html.Lines.ToString(), TextWrapping = TextWrapping.Wrap },
        LinkReferenceDefinitionGroup => null,
        ContainerBlock other => RenderBlocks(other, ctx, spacing: 6),
        LeafBlock { Inline: { } inline } => InlineRenderer.Paragraph(inline, ctx),
        _ => null,
    };

    private static bool IsMermaid(FencedCodeBlock fenced) =>
        string.Equals(fenced.Info?.Trim(), "mermaid", StringComparison.OrdinalIgnoreCase);

    private static Control Heading(HeadingBlock heading, RenderContext ctx)
    {
        var text = InlineRenderer.Paragraph(heading.Inline, ctx);
        text.FontWeight = heading.Level == 1 ? FontWeight.Bold : FontWeight.SemiBold;
        text.FontSize = heading.Level switch { 1 => 29, 2 => 26, 3 => 22, 4 => 20, 5 => 18, _ => 16 };
        text.LineHeight = double.NaN; // paragraph line height would clip the larger font
        text.Margin = new Thickness(0, heading.Level <= 2 ? 24 : 16, 0, 0);
        return text;
    }

    private static Control List(ListBlock list, RenderContext ctx)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;

        var row = 0;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var isTaskItem = IsTaskListItem(item);
            var content = RenderBlocks(item, ctx, spacing: 4);
            content.Margin = new Thickness(0, 0, 0, 4);
            Grid.SetRow(content, row);

            if (isTaskItem)
            {
                // Task items don't show a bullet marker; content spans both columns
                Grid.SetColumnSpan(content, 2);
            }
            else
            {
                // Regular items have a marker in column 0
                var marker = new TextBlock
                {
                    Text = list.IsOrdered ? $"{number++}." : "•",
                    Margin = new Thickness(4, 0, 10, 4),
                    VerticalAlignment = VerticalAlignment.Top,
                    [TextElement.ForegroundProperty] = ctx.Look.TextMuted,
                };
                Grid.SetRow(marker, row);
                grid.Children.Add(marker);

                // Content in column 1
                Grid.SetColumn(content, 1);
            }

            grid.Children.Add(content);
            row++;
        }
        return grid;
    }

    private static bool IsTaskListItem(ListItemBlock item)
    {
        var firstBlock = item.FirstOrDefault();
        if (firstBlock is ParagraphBlock paragraph)
        {
            return paragraph.Inline?.FirstChild is TaskList;
        }
        return false;
    }

    private static Control Quote(QuoteBlock quote, RenderContext ctx) => new Border
    {
        Child = RenderBlocks(quote, ctx, spacing: 6),
        Padding = new Thickness(16, 4, 0, 4),
        BorderThickness = new Thickness(2, 0, 0, 0),
        [Border.BorderBrushProperty] = ctx.Look.Accent,
        [TextElement.ForegroundProperty] = ctx.Look.TextMuted,
    };

    private static Control Code(CodeBlock code, PreviewResources look) => new Border
    {
        Padding = new Thickness(14),
        CornerRadius = new CornerRadius(6),
        [Border.BackgroundProperty] = look.SurfaceCode,
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new SelectableTextBlock
            {
                Text = code.Lines.ToString().TrimEnd('\n'),
                FontFamily = Mono,
                FontSize = 14,
            },
        },
    };

    private static Control Diagram(FencedCodeBlock fenced, RenderContext ctx)
    {
        var source = fenced.Lines.ToString();
        var palette = ctx.Diagram;
        var previous = ctx.Slots.Previous;

        if (ctx.Diagrams.FailureOf(source, palette) is { } failure)
        {
            ctx.Slots.Shown(source, palette);
            return DiagramError(failure, source, ctx.Look);
        }

        // An edited diagram: render the new source off the UI thread and keep the previous picture on
        // screen until it lands, which rebuilds the preview. A note's first showing stays synchronous -
        // there is nothing to show meanwhile, and a blank box that fills in is worse than 5 ms.
        if (!ctx.Diagrams.HasPicture(source, palette)
            && previous is { } shown && ctx.Diagrams.HasPicture(shown.Source, shown.Palette))
        {
            ctx.Slots.Await(source, palette);
            ctx.Diagrams.LoadInBackground(source, palette, () => ctx.DiagramLanded(source, palette));
            ctx.Slots.Shown(shown.Source, shown.Palette);
            return new DiagramView(ctx.Diagrams, shown.Source, shown.Palette);
        }

        ctx.Slots.Shown(source, palette);
        try
        {
            return new DiagramView(ctx.Diagrams, source, palette);
        }
        catch (Exception ex)
        {
            return DiagramError(ex.Message, source, ctx.Look);
        }
    }

    // The diagram source is user input; show the failure where the diagram would be.
    private static Control DiagramError(string message, string source, PreviewResources look) => new Border
    {
        Padding = new Thickness(12),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        [Border.BorderBrushProperty] = look.Error,
        Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "Mermaid: " + message,
                    TextWrapping = TextWrapping.Wrap,
                    [TextBlock.ForegroundProperty] = look.Error,
                },
                new SelectableTextBlock { Text = source.TrimEnd('\n'), FontFamily = Mono, FontSize = 13 },
            },
        },
    };

    private static Control Rule(PreviewResources look) => new Border
    {
        Height = 1,
        Margin = new Thickness(0, 24),
        [Border.BackgroundProperty] = look.Border,
    };

    private static Control TableGrid(Table table, RenderContext ctx)
    {
        var grid = new Grid();
        for (var i = 0; i < table.ColumnDefinitions.Count; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var row = 0;
        foreach (var tableRow in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var column = 0;
            foreach (var cell in tableRow.OfType<TableCell>())
            {
                var border = new Border
                {
                    Child = RenderBlocks(cell, ctx, spacing: 4),
                    Padding = new Thickness(8, 6),
                    BorderThickness = new Thickness(1),
                    [Border.BorderBrushProperty] = ctx.Look.Border,
                };
                if (tableRow.IsHeader)
                {
                    border.SetValue(TextElement.FontWeightProperty, FontWeight.SemiBold);
                    border[Border.BackgroundProperty] = ctx.Look.SurfaceCode;
                }
                Grid.SetRow(border, row);
                Grid.SetColumn(border, column++);
                grid.Children.Add(border);
            }
            row++;
        }

        var wrapper = new Border
        {
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = grid,
            },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            [Border.BorderBrushProperty] = ctx.Look.Border,
        };

        return wrapper;
    }
}
