using Avalonia.Media.Imaging;
using SkiaSharp;

namespace MdNotes.Views.Markdown;

/// <summary>
/// Reuses decoded note images across preview rebuilds. The preview is rebuilt whole every 250 ms while
/// typing, so without this every rebuild re-reads and re-decodes every image on the render pass.
///
/// Bounded by build rather than by count. An <see cref="Avalonia.Controls.Image"/> holds its Source for
/// as long as it is on screen, so it cannot borrow a bitmap for one draw the way <c>DiagramView</c>
/// does - which means a count bound disposes whatever falls out of it, including bitmaps the document's
/// own visual tree is still measuring. Nothing the current build asked for is ever disposed here.
///
/// <see cref="BeginDocument"/> retires a build: a bitmap is disposed only once two builds in a row have
/// gone without it, and by then no tree that could hold it is on screen - the build that used it was
/// replaced, and so was its replacement. The size that follows is one document's images plus one
/// document of lag, which is the cost of the visual tree itself rather than of caching it.
///
/// Keyed by path plus the file's write stamp, so an image edited on disk is decoded again rather than
/// served stale. Failed decodes are not cached.
///
/// Decoded no wider than the preview can show. An image is drawn at most <see cref="DisplayWidth"/> DIP
/// wide, and a 12-megapixel photo decoded at native size is ~48 MB of pixels to draw 800 of them, held up
/// to twice by the build lifetime above. The decode width is the display width in device pixels, so a
/// hi-DPI screen still gets a sharp image, capped at <see cref="MaxScaling"/> times.
/// </summary>
internal sealed class ImageCache : IDisposable
{
    private readonly record struct Key(string Path, long Ticks, long Length);

    /// <summary>The widest an image is shown in the preview, in DIP. The renderer's MaxWidth.</summary>
    public const int DisplayWidth = 800;

    /// <summary>Display scaling past this decodes no sharper; 3x is already beyond common screens.</summary>
    public const double MaxScaling = 3;

    private Dictionary<Key, Bitmap> _current = [];

    /// <summary>
    /// The widest bitmap a decode produces, in pixels. The view sets it from its screen's scaling; a
    /// bitmap already decoded keeps its width until it falls out of the cache.
    /// </summary>
    public int DecodeWidth { get; set; } = DisplayWidth;

    /// <summary>The decode width for a screen's render scaling: the display width in device pixels.</summary>
    public static int DecodeWidthFor(double renderScaling) =>
        (int)Math.Ceiling(DisplayWidth * Math.Clamp(renderScaling, 1, MaxScaling));
    private Dictionary<Key, Bitmap> _retired = [];

    /// <summary>Bitmaps held for the build in progress and for the one before it.</summary>
    public int Count => _current.Count + _retired.Count;

    /// <summary>
    /// Starts a build. What the last one left unused is disposed here rather than when it fell out of
    /// use, because the tree that used it stays on screen until this build replaces it.
    /// </summary>
    public void BeginDocument()
    {
        foreach (var bitmap in _retired.Values) bitmap.Dispose();
        _retired = _current;
        _current = [];
    }

    /// <summary>
    /// The decoded bitmap for a note image. Asking for it is what keeps it: an image the current build
    /// uses is carried over from the retired build rather than decoded again, and is then safe until
    /// two builds have passed without it.
    /// </summary>
    public Bitmap Get(string path)
    {
        var file = new FileInfo(path);
        var key = new Key(path, file.LastWriteTimeUtc.Ticks, file.Length);

        if (_current.TryGetValue(key, out var live)) return live;

        if (_retired.Remove(key, out var carried))
        {
            _current[key] = carried;
            return carried;
        }

        var bitmap = Decode(path);
        _current[key] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Scaled down to <see cref="DecodeWidth"/> while decoding, never up: the header is read first
    /// (no pixels), because <see cref="Bitmap.DecodeToWidth"/> would also enlarge a small image. A file
    /// Skia cannot read the header of goes to the plain decode, which fails the way it always has.
    /// </summary>
    private Bitmap Decode(string path)
    {
        var width = SKBitmap.DecodeBounds(path).Width;
        if (width <= DecodeWidth) return new Bitmap(path);

        using var stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, DecodeWidth, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>Releases every decoded image still held when the owning preview closes.</summary>
    public void Dispose()
    {
        foreach (var bitmap in _current.Values) bitmap.Dispose();
        foreach (var bitmap in _retired.Values) bitmap.Dispose();
        _current.Clear();
        _retired.Clear();
    }
}
