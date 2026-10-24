using Avalonia.Headless.XUnit;
using MdNotes.Views.Markdown;
using SkiaSharp;

namespace MdNotes.Tests;

/// <summary>
/// The preview is rebuilt whole every 250 ms while typing, so the point of the cache is that the second
/// rebuild costs nothing - and that the bitmaps it drops are disposed rather than left to the finalizer.
/// </summary>
public class ImageCacheTests
{
    [AvaloniaFact]
    public void The_same_file_is_decoded_once()
    {
        using var images = new TempImages();
        var path = images.Png("pic.png");
        var cache = new ImageCache();

        Assert.Same(cache.Get(path), cache.Get(path));
        Assert.Equal(1, cache.Count);
    }

    /// <summary>
    /// Finding 11: an image wider than the preview can show is decoded at the width it is shown at, not
    /// at native size - a 12-megapixel photo was ~48 MB of pixels to draw 800 of them.
    /// </summary>
    [AvaloniaFact]
    public void A_wide_image_is_decoded_no_wider_than_it_is_shown()
    {
        using var images = new TempImages();
        var cache = new ImageCache { DecodeWidth = 800 };

        var bitmap = cache.Get(images.Png("wide.png", size: 1600));

        Assert.Equal(800, bitmap.PixelSize.Width);
        Assert.Equal(800, bitmap.PixelSize.Height); // aspect kept
    }

    [AvaloniaFact]
    public void A_small_image_is_not_enlarged()
    {
        using var images = new TempImages();
        var cache = new ImageCache { DecodeWidth = 800 };

        Assert.Equal(100, cache.Get(images.Png("small.png", size: 100)).PixelSize.Width);
    }

    [Theory]
    [InlineData(1.0, 800)]
    [InlineData(1.5, 1200)]
    [InlineData(2.0, 1600)]
    [InlineData(0.5, 800)]   // never below the display width
    [InlineData(10.0, 2400)] // capped
    public void The_decode_width_is_the_display_width_in_device_pixels(double scaling, int expected) =>
        Assert.Equal(expected, ImageCache.DecodeWidthFor(scaling));

    /// <summary>
    /// The reason the bound is not a count: every image in the document is on screen at once, held by an
    /// Image that will measure it again on the next layout pass. A count bound disposed the overflow.
    /// </summary>
    [AvaloniaFact]
    public void No_image_the_current_build_asked_for_is_disposed()
    {
        using var images = new TempImages();
        var cache = new ImageCache();
        cache.BeginDocument();

        var decoded = Enumerable.Range(0, 40).Select(i => cache.Get(images.Png($"pic{i}.png"))).ToList();

        Assert.Equal(40, cache.Count);
        Assert.All(decoded, bitmap => bitmap.CreateScaledBitmap(bitmap.PixelSize).Dispose());
    }

    /// <summary>What the cache exists for: the next keystroke's rebuild must not decode the file again.</summary>
    [AvaloniaFact]
    public void An_image_the_next_build_asks_for_again_is_carried_over()
    {
        using var images = new TempImages();
        var path = images.Png("pic.png");
        var cache = new ImageCache();

        cache.BeginDocument();
        var first = cache.Get(path);
        cache.BeginDocument();

        Assert.Same(first, cache.Get(path));
        Assert.Equal(1, cache.Count);
    }

    /// <summary>
    /// A bitmap is disposed one build late, not when it falls out of use: the tree holding it is on
    /// screen until the build that drops it replaces that tree.
    /// </summary>
    [AvaloniaFact]
    public void An_image_two_builds_have_gone_without_is_disposed()
    {
        using var images = new TempImages();
        var cache = new ImageCache();

        cache.BeginDocument();
        var dropped = cache.Get(images.Png("pic.png"));

        // The build that stops using it: the image it replaces is still drawing, so nothing is freed yet.
        cache.BeginDocument();
        Assert.Equal(1, cache.Count);
        dropped.CreateScaledBitmap(dropped.PixelSize).Dispose();

        // The build after that: the tree that held it was itself replaced, so now it can go.
        cache.BeginDocument();
        Assert.Equal(0, cache.Count);
        Assert.Throws<ObjectDisposedException>(() => dropped.CreateScaledBitmap(dropped.PixelSize));
    }

    /// <summary>An image edited outside the app must not be served stale: the write stamp is part of the key.</summary>
    [AvaloniaFact]
    public void A_rewritten_file_is_decoded_again()
    {
        using var images = new TempImages();
        var path = images.Png("pic.png", size: 8);
        var cache = new ImageCache();
        var first = cache.Get(path);

        images.Png("pic.png", size: 16, colour: SKColors.Goldenrod);
        var second = cache.Get(path);

        Assert.NotSame(first, second);
        Assert.Equal(16, second.PixelSize.Width);
    }
}
