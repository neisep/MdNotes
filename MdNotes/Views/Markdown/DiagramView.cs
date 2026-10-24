using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MdNotes.Models;

namespace MdNotes.Views.Markdown;

/// <summary>
/// Shows one Mermaid diagram as a bitmap rasterized at its displayed pixel width. Sized to the diagram's natural
/// size, shrunk to fit the available width. While the pane is being resized the current bitmap is drawn
/// stretched; once the size has settled it is re-rasterized so the result is pixel-exact at any width and DPI.
///
/// The view keeps the cache <em>key</em>, never the bitmap. The cache owns bitmap lifetimes and evicts on its
/// own schedule, so a view holding one between frames would eventually draw one the cache had already disposed
/// - a key owns nothing and cannot go stale that way.
/// </summary>
internal sealed class DiagramView : Control
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    private readonly DiagramCache _cache;
    private readonly string _source;
    private readonly DiagramPalette _palette;
    private readonly Size _natural;
    private readonly DispatcherTimer _settle = new() { Interval = Settle };

    /// <summary>The Mermaid source this view draws - which, while an edit renders, is the previous one.</summary>
    internal string Source => _source;

    /// <summary>The width the bitmap is rasterized at, in device pixels; 0 before the first layout pass.</summary>
    private int _pixelWidth;

    public DiagramView(DiagramCache cache, string source, DiagramPalette palette)
    {
        _cache = cache;
        _source = source;
        _palette = palette;
        _natural = cache.NaturalSize(source, palette);
        HorizontalAlignment = HorizontalAlignment.Left;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Rasterize();
        };
    }

    /// <summary>
    /// The bitmap for the settled width, borrowed from the cache; null before the first layout pass.
    /// A method rather than a property because a miss rasterizes, which is not what a property should cost.
    /// </summary>
    public Bitmap? CurrentBitmap() =>
        _pixelWidth > 0 ? _cache.GetBitmap(_source, _palette, _pixelWidth) : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? _natural.Width : Math.Min(_natural.Width, availableSize.Width);
        return new Size(width, width * _natural.Height / _natural.Width);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_pixelWidth == 0)
        {
            Rasterize();
            return;
        }
        _settle.Stop();
        _settle.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _settle.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    // Borrowed for the length of this call alone. Avalonia takes its own reference on the draw it
    // records, so what the compositor does with the bitmap afterwards is not this view's problem.
    public override void Render(DrawingContext context)
    {
        if (CurrentBitmap() is { } bitmap)
            context.DrawImage(bitmap, new Rect(Bounds.Size));
    }

    /// <summary>
    /// Points the view at the bitmap for the current width and display scaling, and renders it now so a
    /// resize does not pay for it on the draw path.
    /// </summary>
    public void Rasterize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixelWidth = (int)Math.Round(Bounds.Width * scaling);
        if (pixelWidth <= 0) return;
        _pixelWidth = pixelWidth;
        _cache.GetBitmap(_source, _palette, _pixelWidth);
        InvalidateVisual();
    }
}
