using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

public class DiagramViewTests
{
    private const string Markdown = "```mermaid\nflowchart LR\n  A[Start] --> B{Decision}\n  B -->|Yes| C[Done]\n```";

    [AvaloniaFact]
    public void Rasterizes_at_displayed_width_and_again_after_resize()
    {
        var renderer = new DiagramCacheTests.CountingRenderer();
        var view = new MarkdownView { DiagramRenderer = renderer, Document = new MarkdownRenderer().Parse(Markdown) };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.CaptureRenderedFrame();

        var diagram = view.GetVisualDescendants().OfType<DiagramView>().Single();
        var natural = diagram.Bounds.Width;
        Assert.NotNull(diagram.CurrentBitmap());
        Assert.Equal((int)Math.Round(natural), diagram.CurrentBitmap()!.PixelSize.Width);

        window.Width = 300; // narrower than the diagram: it must shrink and re-rasterize at the new width
        window.CaptureRenderedFrame();
        diagram.Rasterize(); // what the settle timer does once resizing stops

        Assert.True(diagram.Bounds.Width < natural);
        Assert.Equal((int)Math.Round(diagram.Bounds.Width), diagram.CurrentBitmap()!.PixelSize.Width);
        Assert.Equal(1, renderer.Calls); // resizing re-rasterizes the cached picture, it does not re-run Mermaid
        window.Close();
    }

    // F11: the cache disposes on eviction and knows nothing about what is on screen. With the bitmap
    // bound set to one, rasterizing the second diagram evicts the first - which is still visible.
    [AvaloniaFact]
    public void A_visible_diagram_survives_another_ones_rasterization()
    {
        var cache = new DiagramCache(new DiagramCacheTests.CountingRenderer(), bitmapCapacity: 1);
        var palette = new DiagramPalette("#FFFFFF", "#222222");
        var first = new DiagramView(cache, "flowchart LR\n A --> B", palette);
        var second = new DiagramView(cache, "flowchart LR\n C --> D", palette);

        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = new StackPanel { Children = { first, second } },
        };
        window.Show();
        window.CaptureRenderedFrame();

        // Both are on screen, so drawing either one must still work after the other was rasterized.
        first.Rasterize();
        second.Rasterize();
        Assert.NotNull(window.CaptureRenderedFrame());

        window.Close();
    }
}
