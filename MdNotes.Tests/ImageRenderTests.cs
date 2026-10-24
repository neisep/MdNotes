using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

/// <summary>
/// Images end to end through the real preview: what renders, what is refused, and that a rebuild does not
/// decode the file again. The preview is rebuilt whole on a 250 ms timer while typing, so the last point
/// is the one the user feels.
/// </summary>
public class ImageRenderTests
{
    [AvaloniaFact]
    public void An_image_under_the_content_root_renders()
    {
        using var images = new TempImages();
        images.Png("pic.png");
        var (window, view) = Show("![a picture](pic.png)", images.Root, images.Root);

        var image = Assert.Single(view.GetVisualDescendants().OfType<Image>());
        Assert.NotNull(image.Source);

        window.Close();
    }

    /// <summary>An attachments folder beside the note's folder is the ordinary vault layout.</summary>
    [AvaloniaFact]
    public void An_image_above_the_note_but_inside_the_root_renders()
    {
        using var images = new TempImages();
        images.Png("assets/pic.png");
        var noteFolder = Path.Combine(images.Root, "notes");
        Directory.CreateDirectory(noteFolder);

        var (window, view) = Show("![a picture](../assets/pic.png)", noteFolder, images.Root);

        Assert.Single(view.GetVisualDescendants().OfType<Image>());
        window.Close();
    }

    [AvaloniaFact]
    public void An_image_outside_the_content_root_is_not_loaded()
    {
        using var images = new TempImages();
        using var elsewhere = new TempImages();
        elsewhere.Png("secret.png");
        var noteFolder = Path.Combine(images.Root, "notes");
        Directory.CreateDirectory(noteFolder);

        var relative = Path.GetRelativePath(noteFolder, Path.Combine(elsewhere.Root, "secret.png"));
        var (window, view) = Show($"![a picture]({relative.Replace('\\', '/')})", noteFolder, images.Root);

        Assert.Empty(view.GetVisualDescendants().OfType<Image>());
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Inlines?.Text?.Contains("[image: a picture]") == true);

        window.Close();
    }

    /// <summary>
    /// Without a root the note's own folder contains the image, so the preview is never a way to read
    /// anywhere on disk - not even before a vault has been opened.
    /// </summary>
    [AvaloniaFact]
    public void Without_a_content_root_an_image_above_the_note_is_not_loaded()
    {
        using var images = new TempImages();
        images.Png("assets/pic.png");
        var noteFolder = Path.Combine(images.Root, "notes");
        Directory.CreateDirectory(noteFolder);

        var (window, view) = Show("![a picture](../assets/pic.png)", noteFolder, contentRoot: null);

        Assert.Empty(view.GetVisualDescendants().OfType<Image>());
        window.Close();
    }

    [AvaloniaFact]
    public void Rebuilding_the_preview_reuses_the_decoded_bitmap()
    {
        using var images = new TempImages();
        images.Png("pic.png");
        var (window, view) = Show("![a picture](pic.png)", images.Root, images.Root);
        var first = Assert.Single(view.GetVisualDescendants().OfType<Image>()).Source;

        // What the 250 ms preview timer does on the next keystroke: a brand-new document, rendered whole.
        view.Document = new MarkdownRenderer().Parse("![a picture](pic.png)\n\nand more text");
        window.CaptureRenderedFrame();
        var second = Assert.Single(view.GetVisualDescendants().OfType<Image>()).Source;

        Assert.Same(first, second);
        Assert.IsType<Bitmap>(first);

        window.Close();
    }

    /// <summary>
    /// An <see cref="Image"/> holds its Source across frames, so a cache bounded by count disposes
    /// bitmaps the document's own visual tree is still measuring. Thirty images in one note is an
    /// ordinary screenshot log; against the count bound of 24 this threw
    /// <see cref="ObjectDisposedException"/> out of <c>Image.MeasureOverride</c> on the first layout pass.
    /// </summary>
    [AvaloniaFact]
    public void A_note_with_more_images_than_a_count_bound_still_renders()
    {
        using var images = new TempImages();
        var markdown = string.Join("\n\n", Enumerable.Range(0, 30).Select(i =>
        {
            images.Png($"pic{i}.png");
            return $"![picture {i}](pic{i}.png)";
        }));

        var (window, view) = Show(markdown, images.Root, images.Root);

        Assert.Equal(30, view.GetVisualDescendants().OfType<Image>().Count());
        Assert.NotNull(window.CaptureRenderedFrame());

        window.Close();
    }

    private static (Window Window, MarkdownView View) Show(string markdown, string? baseDirectory, string? contentRoot)
    {
        var view = new MarkdownView
        {
            DiagramRenderer = new MermaiderDiagramRenderer(),
            Document = new MarkdownRenderer().Parse(markdown),
            BaseDirectory = baseDirectory,
            ContentRoot = contentRoot,
            Margin = new Thickness(24),
        };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        window.CaptureRenderedFrame();
        return (window, view);
    }
}
