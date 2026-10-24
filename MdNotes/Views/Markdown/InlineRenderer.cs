using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdNotes.Services;
using MdNotes.Services.Markdown;

namespace MdNotes.Views.Markdown;

/// <summary>Turns a Markdig inline tree into one wrapping TextBlock, recording clickable link ranges.</summary>
internal static class InlineRenderer
{
    public static TextBlock Paragraph(ContainerInline? inline, RenderContext ctx)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            LineHeight = 26,
            [TextElement.ForegroundProperty] = ctx.Look.Text,
        };
        var links = new List<LinkRange>();
        if (inline is not null)
            new Builder(links, ctx).AddChildren(inline, text.Inlines!);
        if (links.Count > 0)
            LinkHitTesting.Attach(text, links, ctx.OpenLink);
        return text;
    }

    private sealed class Builder(List<LinkRange> links, RenderContext ctx)
    {
        private int _position;

        public void AddChildren(ContainerInline container, InlineCollection target)
        {
            foreach (var child in container)
                Add(child, target);
        }

        private void Add(Markdig.Syntax.Inlines.Inline inline, InlineCollection target)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AddRun(target, literal.Content.ToString());
                    break;
                case LineBreakInline lineBreak:
                    AddRun(target, lineBreak.IsHard ? "\n" : " ");
                    break;
                case CodeInline code:
                    AddRun(target, code.Content, run =>
                    {
                        run.FontFamily = BlockRenderer.Mono;
                        run[TextElement.ForegroundProperty] = ctx.Look.TextMuted;
                        run[TextElement.BackgroundProperty] = ctx.Look.SurfaceCode;
                    });
                    break;
                case EmphasisInline emphasis:
                    AddSpan(target, emphasis, span => ApplyEmphasis(span, emphasis, ctx.Look));
                    break;
                case LinkInline { IsImage: true } image:
                    AddImage(target, image);
                    break;
                case LinkInline link:
                    AddLink(target, link, link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty,
                        link.GetAttributes().Classes?.Contains(WikiLinkExtension.FolderClass) == true);
                    break;
                case AutolinkInline autolink:
                    AddLink(target, null, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, false, autolink.Url);
                    break;
                case TaskList task:
                    AddTaskListCheckbox(target, task.Checked);
                    break;
                case HtmlEntityInline entity:
                    AddRun(target, entity.Transcoded.ToString());
                    break;
                case HtmlInline:
                    break; // raw HTML has no native rendering; dropped from the preview
                case ContainerInline container:
                    AddChildren(container, target);
                    break;
                default:
                    AddRun(target, inline.ToString() ?? string.Empty);
                    break;
            }
        }

        private void AddRun(InlineCollection target, string text, Action<Run>? configure = null)
        {
            var run = new Run(text);
            configure?.Invoke(run);
            target.Add(run);
            _position += text.Length;
        }

        private void AddSpan(InlineCollection target, ContainerInline children, Action<Span> configure)
        {
            var span = new Span();
            configure(span);
            target.Add(span);
            AddChildren(children, span.Inlines);
        }

        private void AddLink(InlineCollection target, ContainerInline? children, string url, bool isFolderLink, string? literal = null)
        {
            var start = _position;
            var span = new Span
            {
                TextDecorations = TextDecorations.Underline,
                [TextElement.ForegroundProperty] = ctx.Look.Accent,
            };
            target.Add(span);
            // Inside the span, so the glyph falls within the recorded range and is clickable like the text.
            if (isFolderLink) AddFolderGlyph(span.Inlines);
            if (children is not null) AddChildren(children, span.Inlines);
            if (literal is not null) AddRun(span.Inlines, literal);
            links.Add(new LinkRange(start, _position, url));
        }

        private void AddFolderGlyph(InlineCollection target)
        {
            var icon = new Avalonia.Controls.Shapes.Path
            {
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                Fill = null,
                Margin = new Thickness(1, 0, 5, 0),
                [Avalonia.Controls.Shapes.Path.DataProperty] = ctx.Look.FolderIcon,
                [Avalonia.Controls.Shapes.Shape.StrokeProperty] = ctx.Look.Accent,
            };

            target.Add(new InlineUIContainer { Child = icon, BaselineAlignment = BaselineAlignment.Center });
            _position += 1; // an embedded control occupies one character
        }

        private void AddImage(InlineCollection target, LinkInline image)
        {
            var alt = string.Concat(image.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
            // Not found, not a file, or outside the content root - all the same to the reader: no image.
            var path = NoteImagePath.Resolve(ctx.BaseDirectory, ctx.ContentRoot, image.Url);
            if (path is null)
            {
                AddRun(target, $"[image: {alt}]");
                return;
            }

            try
            {
                target.Add(new InlineUIContainer
                {
                    BaselineAlignment = BaselineAlignment.Bottom,
                    Child = new Image
                    {
                        Source = ctx.Images.Get(path),
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        MaxWidth = ImageCache.DisplayWidth,
                    },
                });
                _position += 1; // an embedded control occupies one character in the text layout
            }
            catch (Exception ex)
            {
                AddRun(target, $"[image '{alt}' could not be loaded: {ex.Message}]");
            }
        }

        private void AddTaskListCheckbox(InlineCollection target, bool isChecked)
        {
            var icon = new Avalonia.Controls.Shapes.Path
            {
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                Fill = null,
                Margin = new Thickness(0, 0, 6, 0),
                [Avalonia.Controls.Shapes.Path.DataProperty] = isChecked ? ctx.Look.CheckedIcon : ctx.Look.UncheckedIcon,
                [Avalonia.Controls.Shapes.Shape.StrokeProperty] = isChecked ? ctx.Look.Accent : ctx.Look.TextMuted,
            };

            target.Add(new InlineUIContainer { Child = icon, BaselineAlignment = BaselineAlignment.Center });
            _position += 1; // an embedded control occupies one character
        }

        private static void ApplyEmphasis(Span span, EmphasisInline emphasis, PreviewResources look)
        {
            switch (emphasis.DelimiterChar, emphasis.DelimiterCount)
            {
                case ('*' or '_', >= 2): span.FontWeight = FontWeight.Bold; break;
                case ('*' or '_', _): span.FontStyle = FontStyle.Italic; break;
                case ('~', _): span.TextDecorations = TextDecorations.Strikethrough; break;
                case ('+', _): span.TextDecorations = TextDecorations.Underline; break;
                case ('=', _): span[TextElement.BackgroundProperty] = look.SurfaceCode; break;
            }
        }
    }
}
