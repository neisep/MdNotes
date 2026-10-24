using Avalonia.Headless.XUnit;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

public class DiagramCacheTests
{
    private const string A = "flowchart LR\n A --> B";
    private const string C = "flowchart LR\n C --> D";
    private const string E = "flowchart LR\n E --> F";

    // Two palettes stand in for the two themes: the cache keys on the colours, not on a flag.
    private static readonly DiagramPalette Light = new("#FFFFFF", "#222222");
    private static readonly DiagramPalette Dark = new("#1E1E1E", "#DADADA");

    [AvaloniaFact]
    public void Same_source_and_palette_is_rendered_once()
    {
        var renderer = new CountingRenderer();
        var cache = new DiagramCache(renderer);

        var first = cache.GetPicture(A, Light);
        var second = cache.GetPicture(A, Light);
        cache.GetPicture(A, Dark);

        Assert.Same(first, second);
        Assert.Equal(2, renderer.Calls);
        Assert.Equal(2, cache.PictureCount);
    }

    [AvaloniaFact]
    public void Evicts_least_recently_used_picture_when_full()
    {
        var renderer = new CountingRenderer();
        var cache = new DiagramCache(renderer, pictureCapacity: 2);

        cache.GetPicture(A, Light);
        cache.GetPicture(C, Light);
        cache.GetPicture(A, Light); // touch A so C is the oldest
        cache.GetPicture(E, Light); // evicts C

        Assert.Equal(2, cache.PictureCount);
        cache.GetPicture(A, Light);
        Assert.Equal(3, renderer.Calls);
        cache.GetPicture(C, Light);
        Assert.Equal(4, renderer.Calls);
    }

    [AvaloniaFact]
    public void Failed_renders_are_not_cached()
    {
        var renderer = new CountingRenderer();
        var cache = new DiagramCache(renderer);

        Assert.ThrowsAny<Exception>(() => cache.GetPicture("notADiagram", Light));
        Assert.ThrowsAny<Exception>(() => cache.GetPicture("notADiagram", Light));
        Assert.Equal(2, renderer.Calls);
        Assert.Equal(0, cache.PictureCount);
    }

    [AvaloniaFact]
    public void Bitmaps_are_rasterized_per_pixel_width_and_keep_the_aspect_ratio()
    {
        var cache = new DiagramCache(new CountingRenderer());
        var natural = cache.NaturalSize(A, Light);

        var wide = cache.GetBitmap(A, Light, 600);
        var narrow = cache.GetBitmap(A, Light, 300);
        var again = cache.GetBitmap(A, Light, 600);

        Assert.Same(wide, again);
        Assert.Equal(600, wide.PixelSize.Width);
        Assert.Equal(300, narrow.PixelSize.Width);
        Assert.Equal((int)Math.Ceiling(natural.Height * 600 / natural.Width), wide.PixelSize.Height);
        Assert.Equal(2, cache.BitmapCount);
    }

    [AvaloniaFact]
    public void Dispose_releases_both_maps()
    {
        var cache = new DiagramCache(new CountingRenderer());
        cache.GetBitmap(A, Light, 600);

        cache.Dispose();

        Assert.Equal(0, cache.PictureCount);
        Assert.Equal(0, cache.BitmapCount);
    }

    internal sealed class CountingRenderer : IDiagramRenderer
    {
        private readonly MermaiderDiagramRenderer _inner = new();
        public int Calls { get; private set; }

        public string RenderSvg(string mermaidSource, DiagramPalette palette)
        {
            Calls++;
            return _inner.RenderSvg(mermaidSource, palette);
        }
    }
}
