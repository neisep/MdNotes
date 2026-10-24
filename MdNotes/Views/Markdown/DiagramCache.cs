using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using MdNotes.Models;
using MdNotes.Services;
using SkiaSharp;

namespace MdNotes.Views.Markdown;

/// <summary>
/// Reuses rendered diagrams across preview rebuilds and resizes. Pictures (vector) are kept because every
/// <see cref="SvgSource"/> load leaks a little unmanaged memory inside Svg.Skia; bitmaps are kept per displayed
/// pixel width so scrolling blits a texture instead of replaying vector commands every frame.
/// Both maps are bounded LRUs whose evicted entries are disposed.
///
/// A diagram can also be rendered off the UI thread (<see cref="LoadInBackground"/>): Mermaid plus the
/// SVG load measured 4-9 ms, paid on every preview tick while someone types inside a fence. A
/// background render's failure is remembered, because the view rebuilds when a render lands and a
/// failure it forgot would be retried by that rebuild forever. A synchronous failure is not cached.
/// </summary>
internal sealed class DiagramCache(IDiagramRenderer renderer, int pictureCapacity = 64, int bitmapCapacity = 32)
    : IDisposable
{
    private readonly Lru<(string Source, DiagramPalette Palette), SvgSource> _pictures = new(pictureCapacity);
    private readonly Lru<(string Source, DiagramPalette Palette, int PixelWidth), Bitmap> _bitmaps = new(bitmapCapacity);

    public IDiagramRenderer Renderer { get; } = renderer;

    /// <summary>
    /// One render at a time, process-wide, synchronous or not. Before background rendering every render
    /// ran on the UI thread and so never overlapped another; Mermaider and Svg.Skia were never asked to
    /// be thread-safe, and this keeps it that way. The UI thread waits at most one render here.
    /// </summary>
    private static readonly Lock RenderGate = new();

    private const int FailureCapacity = 64;

    private readonly HashSet<(string Source, DiagramPalette Palette)> _loading = [];
    private readonly Dictionary<(string Source, DiagramPalette Palette), string> _failures = [];
    private bool _disposed;

    public int PictureCount => _pictures.Count;
    public int BitmapCount => _bitmaps.Count;

    public SvgSource GetPicture(string source, DiagramPalette palette) =>
        _pictures.GetOrAdd((source, palette), () => Load(source, palette));

    /// <summary>Whether the diagram's picture is ready, so showing it costs no render.</summary>
    public bool HasPicture(string source, DiagramPalette palette) => _pictures.Contains((source, palette));

    /// <summary>Why a background render of this diagram failed, or null if none has.</summary>
    public string? FailureOf(string source, DiagramPalette palette) =>
        _failures.GetValueOrDefault((source, palette));

    /// <summary>
    /// Renders the diagram on the thread pool. When it lands - back on the UI thread -
    /// <paramref name="landed"/> says whether it is still wanted: if so the picture (or the failure) is
    /// kept and the caller is expected to show it; if not, the picture is disposed rather than cached,
    /// so the half-typed states someone types past are never held. A render already in flight for the
    /// same diagram is not started twice.
    /// </summary>
    public void LoadInBackground(string source, DiagramPalette palette, Func<bool> landed)
    {
        var key = (source, palette);
        if (_disposed || !_loading.Add(key)) return;
        _ = LoadAsync(key, landed);
    }

    private async Task LoadAsync((string Source, DiagramPalette Palette) key, Func<bool> landed)
    {
        SvgSource? picture = null;
        string? failure = null;
        try
        {
            picture = await Task.Run(() => Load(key.Source, key.Palette));
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        _loading.Remove(key);
        if (_disposed || !landed())
        {
            picture?.Dispose();
            return;
        }

        if (picture is not null)
        {
            // Rendered synchronously meanwhile: keep the one the cache already hands out.
            if (!ReferenceEquals(_pictures.GetOrAdd(key, () => picture), picture)) picture.Dispose();
            return;
        }

        if (_failures.Count >= FailureCapacity) _failures.Clear();
        _failures[key] = failure ?? "The diagram could not be rendered.";
    }

    private SvgSource Load(string source, DiagramPalette palette)
    {
        lock (RenderGate) return SvgSource.LoadFromSvg(Renderer.RenderSvg(source, palette));
    }

    /// <summary>Natural size of the diagram in device-independent pixels (SVG user units).</summary>
    public Size NaturalSize(string source, DiagramPalette palette)
    {
        var rect = Picture(source, palette).CullRect;
        return new Size(rect.Width, rect.Height);
    }

    public Bitmap GetBitmap(string source, DiagramPalette palette, int pixelWidth) =>
        _bitmaps.GetOrAdd((source, palette, pixelWidth), () => Rasterize(Picture(source, palette), pixelWidth));

    private SKPicture Picture(string source, DiagramPalette palette) =>
        GetPicture(source, palette).Picture
        ?? throw new InvalidOperationException("The diagram SVG produced no picture.");

    /// <summary>Releases both maps. The Svg.Skia pictures are unmanaged memory, so dropping the
    /// cache without this is the leak the cache exists to prevent, just at a coarser grain.</summary>
    public void Dispose()
    {
        _disposed = true;
        _pictures.Dispose();
        _bitmaps.Dispose();
    }

    private static Bitmap Rasterize(SKPicture picture, int pixelWidth)
    {
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The diagram has no size.");

        var scale = pixelWidth / bounds.Width;
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        var bitmap = new WriteableBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        using var buffer = bitmap.Lock();
        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, buffer.Address, buffer.RowBytes);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();
        return bitmap;
    }
}
