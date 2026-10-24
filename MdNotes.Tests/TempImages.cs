using SkiaSharp;

namespace MdNotes.Tests;

/// <summary>A temp folder holding real PNGs, deleted on dispose. Avalonia decodes them for real, so they
/// have to be genuine files rather than named bytes.</summary>
public sealed class TempImages : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("mdnotes-img-").FullName;

    public void Dispose() => Directory.Delete(Root, recursive: true);

    /// <summary>Writes a solid PNG and returns its full path. The size is the only thing tests vary.</summary>
    public string Png(string relativePath, int size = 8, SKColor? colour = null)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encode(size, colour ?? SKColors.MediumPurple));
        return path;
    }

    private static byte[] Encode(int size, SKColor colour)
    {
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(colour);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
