using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

public class MarkdownViewTests
{
    private const string Demo = """
        # Demo

        Text with **bold**, _italic_, `code`, a [[Second Note|wikilink]], a [[Projects/]] folder link and a [link](https://example.com).

        ```mermaid
        flowchart LR
          A[Start] --> B{Decision}
          B -->|Yes| C[Done]
        ```

        ```mermaid
        sequenceDiagram
          Alice->>Bob: Hello
        ```

        1. First
        2. Second
        - [x] done
        - [ ] open

        > Quoted text

        | Feature | Status |
        |---------|--------|
        | Wikilinks | done |
        | Mermaid | done |
        | Tables | done |

        ```csharp
        public sealed record SearchHit(string FullPath, string Name, string Snippet);
        ```

        ---

        ```mermaid
        notADiagram
          foo --> bar
        ```
        """;

    [AvaloniaFact]
    public void Renders_all_block_types_and_shows_diagram_errors_inline()
    {
        var (window, view) = Show(Demo);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("MDNOTES_RENDER_OUT") is { Length: > 0 } dir)
            frame.Save(Path.Combine(dir, "markdown-view.png"), new PngBitmapEncoderOptions());

        var visuals = view.GetVisualDescendants().ToList();
        Assert.Equal(2, visuals.OfType<DiagramView>().Count(d => d.CurrentBitmap() is not null));
        Assert.Contains(visuals.OfType<TextBlock>(), t => t.Text?.StartsWith("Mermaid: ") == true);
        Assert.Contains(visuals.OfType<Grid>(), g => g.ColumnDefinitions.Count == 2 && g.RowDefinitions.Count == 4);
        Assert.Contains(visuals.OfType<SelectableTextBlock>(), t => t.Text?.Contains("record SearchHit") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_wikilink_runs_the_link_command_with_the_note_name()
    {
        string? opened = null;
        var (window, view) = Show("Go to [[Second Note|the second note]] now.", new RelayCommand<string>(n => opened = n));
        window.CaptureRenderedFrame();

        var paragraph = view.GetVisualDescendants().OfType<TextBlock>().Single();
        var index = paragraph.Inlines!.Text!.IndexOf("second", StringComparison.Ordinal);
        var glyph = paragraph.TextLayout.HitTestTextPosition(index);
        var point = paragraph.TranslatePoint(glyph.Center, window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal("Second Note", opened);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_folder_wikilink_keeps_the_trailing_slash_and_shows_a_folder_glyph()
    {
        string? opened = null;
        var (window, view) = Show("Go to [[Projects/]] now.", new RelayCommand<string>(n => opened = n));
        window.CaptureRenderedFrame();

        var paragraph = view.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Single(view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());

        var index = paragraph.Inlines!.Text!.IndexOf("Projects", StringComparison.Ordinal);
        var glyph = paragraph.TextLayout.HitTestTextPosition(index);
        var point = paragraph.TranslatePoint(glyph.Center, window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal("Projects/", opened);
        window.Close();
    }

    /// <summary>
    /// Frontmatter is metadata, not content. Before UseYamlFrontMatter it parsed as a rule plus a setext
    /// heading, so a templated note announced its own 'created:' line as a title.
    /// </summary>
    [AvaloniaFact]
    public void Yaml_frontmatter_is_not_rendered()
    {
        var (window, view) = Show("---\ncreated: 2026-09-18\ntags: [note]\n---\n\n# Meeting\n");
        window.CaptureRenderedFrame();

        var visuals = view.GetVisualDescendants().ToList();
        Assert.DoesNotContain(visuals.OfType<Border>(), b => b.Height is 1);
        Assert.DoesNotContain(visuals.OfType<TextBlock>(), t => t.Text?.Contains("created:") == true);
        Assert.DoesNotContain(visuals.OfType<SelectableTextBlock>(), t => t.Text?.Contains("created:") == true);
        Assert.Contains(visuals.OfType<TextBlock>(), t => t.Inlines?.Text == "Meeting");

        window.Close();
    }

    // The renderer used to be a static on the control, so swapping it left the previous cache behind
    // and every test that built a window had to write it. It is a property now, and changing it
    // rebuilds through the new renderer instead of serving what the old one had cached.
    [AvaloniaFact]
    public void Replacing_the_renderer_rebuilds_the_diagram_through_it()
    {
        const string Mermaid = "```mermaid\nflowchart LR\n A --> B\n```";
        var first = new DiagramCacheTests.CountingRenderer();
        var second = new DiagramCacheTests.CountingRenderer();

        var view = new MarkdownView
        {
            DiagramRenderer = first,
            Document = new MarkdownRenderer().Parse(Mermaid),
        };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        window.CaptureRenderedFrame();
        Assert.Equal(1, first.Calls);

        view.DiagramRenderer = second;
        window.CaptureRenderedFrame();

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        window.Close();
    }

    private static (Window Window, MarkdownView View) Show(string markdown, ICommand? linkCommand = null)
    {
        var view = new MarkdownView
        {
            DiagramRenderer = new MermaiderDiagramRenderer(),
            Document = new MarkdownRenderer().Parse(markdown),
            LinkCommand = linkCommand,
            Margin = new Thickness(24),
        };
        var window = new Window { Width = 900, Height = 1500, Content = view };
        window.Show();
        return (window, view);
    }
}
