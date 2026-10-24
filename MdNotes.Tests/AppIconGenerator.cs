using SkiaSharp;

namespace MdNotes.Tests;

/// <summary>
/// Draws the MdNotes app icon and writes <c>MdNotes/Assets/mdnotes.ico</c> (PNG-compressed entries at all
/// Windows sizes). Runs only when <c>MDNOTES_GENERATE_ICON</c> is set so the source tree is never touched by a
/// normal test run. Also writes <c>icon-256.png</c> and <c>icon-16x8.png</c> to MDNOTES_RENDER_OUT for review.
/// </summary>
public class AppIconGenerator
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [Fact]
    public void Generate()
    {
        var outDir = Environment.GetEnvironmentVariable("MDNOTES_GENERATE_ICON");
        Assert.SkipWhen(string.IsNullOrEmpty(outDir), "set MDNOTES_GENERATE_ICON=<repo>/MdNotes/Assets to regenerate the icon");

        var pngs = Sizes.Select(size => (size, Png: Render(size))).ToList();
        File.WriteAllBytes(Path.Combine(outDir!, "mdnotes.ico"), PackIco(pngs));

        if (Environment.GetEnvironmentVariable("MDNOTES_RENDER_OUT") is { Length: > 0 } review)
        {
            File.WriteAllBytes(Path.Combine(review, "icon-256.png"), pngs.Single(p => p.size == 256).Png);
            File.WriteAllBytes(Path.Combine(review, "icon-16x8.png"), Upscale(pngs.Single(p => p.size == 16).Png, 8));
        }
    }

    private static byte[] Render(int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        var s = size / 256f; // design at 256, scale down

        // Rounded square with the app's purple gradient (light accent → dark accent).
        var rect = new SKRect(8 * s, 8 * s, 248 * s, 248 * s);
        using var bg = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                [new SKColor(0xA7, 0x8B, 0xFA), new SKColor(0x6B, 0x57, 0xE0)], SKShaderTileMode.Clamp),
        };
        c.DrawRoundRect(rect, 56 * s, 56 * s, bg);

        // White "M" with a down arrow (the Markdown mark), stroked so it stays crisp at 16 px.
        using var ink = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.5f, 28 * s),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        var m = new SKPathBuilder();
        m.MoveTo(42 * s, 176 * s);
        m.LineTo(42 * s, 80 * s);
        m.LineTo(86 * s, 134 * s);
        m.LineTo(130 * s, 80 * s);
        m.LineTo(130 * s, 176 * s);
        using var mPath = m.Detach();
        c.DrawPath(mPath, ink);

        var arrow = new SKPathBuilder();
        arrow.MoveTo(198 * s, 80 * s);
        arrow.LineTo(198 * s, 176 * s);
        arrow.MoveTo(172 * s, 150 * s);
        arrow.LineTo(198 * s, 176 * s);
        arrow.LineTo(224 * s, 150 * s);
        using var arrowPath = arrow.Detach();
        c.DrawPath(arrowPath, ink);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ICO container: ICONDIR + one ICONDIRENTRY per image, followed by the PNG payloads.
    private static byte[] PackIco(List<(int size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size == 256 ? 0 : size)); w.Write((byte)(size == 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(png.Length); w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
        return stream.ToArray();
    }

    private static byte[] Upscale(byte[] png, int factor)
    {
        using var src = SKBitmap.Decode(png);
        using var dst = new SKBitmap(src.Width * factor, src.Height * factor);
        using var canvas = new SKCanvas(dst);
        using var image0 = SKImage.FromBitmap(src);
        canvas.DrawImage(image0, new SKRect(0, 0, dst.Width, dst.Height), new SKSamplingOptions(SKFilterMode.Nearest));
        using var image = SKImage.FromBitmap(dst);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
