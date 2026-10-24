using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

/// <summary>
/// Finding 8: the preview's brushes are resolved once per build instead of bound live per element. The
/// live binding is what used to carry a theme switch, so what has to hold now is that the rebuild a
/// theme switch causes paints with the new theme.
/// </summary>
public class PreviewResourcesTests
{
    [AvaloniaFact]
    public void Switching_the_theme_repaints_the_preview_in_the_new_theme()
    {
        var view = new MarkdownView
        {
            DiagramRenderer = new MermaiderDiagramRenderer(),
            Document = new MarkdownRenderer().Parse("Some text, and `code`."),
        };
        var window = new Window { Content = view, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var light = Foreground(view);

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = Foreground(view);

        Assert.True(view.TryFindResource("MdText", ThemeVariant.Dark, out var expected));
        Assert.NotEqual(light, dark);
        Assert.Equal(((ISolidColorBrush)expected!).Color, dark);
        window.Close();
    }

    private static Color Foreground(MarkdownView view) =>
        ((ISolidColorBrush)view.GetVisualDescendants().OfType<TextBlock>().Single().Foreground!).Color;
}
