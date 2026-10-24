using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

/// <summary>
/// Finding 7: in Edit-only mode the preview's container is hidden, and building its control tree every
/// 250 ms of typing bought nothing. A hidden view owes the rebuild instead, and pays it once on being shown.
/// The view's own IsVisible is what it watches (MainWindow binds it beside its container's), so that is
/// what these toggle.
/// </summary>
public class HiddenPreviewTests
{
    private static readonly MarkdownRenderer Renderer = new();

    [AvaloniaFact]
    public void A_hidden_preview_builds_nothing_and_renders_the_latest_text_once_when_shown()
    {
        var view = new MarkdownView { DiagramRenderer = new MermaiderDiagramRenderer(), Document = Renderer.Parse("first") };
        var host = new Border { Child = view };
        var window = new Window { Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var shown = view.Rebuilds;

        view.IsVisible = false;
        foreach (var text in new[] { "second", "third", "fourth" })
        {
            view.Document = Renderer.Parse(text);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(shown, view.Rebuilds);

        view.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(shown + 1, view.Rebuilds);
        Assert.Equal("fourth", Assert.Single(view.GetVisualDescendants().OfType<TextBlock>()).Inlines!.Text);
        window.Close();
    }

    /// <summary>Shown again with nothing changed while hidden: there is nothing owed, so nothing is built.</summary>
    [AvaloniaFact]
    public void Showing_a_preview_that_missed_nothing_does_not_rebuild_it()
    {
        var view = new MarkdownView { DiagramRenderer = new MermaiderDiagramRenderer(), Document = Renderer.Parse("text") };
        var host = new Border { Child = view };
        var window = new Window { Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var shown = view.Rebuilds;

        view.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        view.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(shown, view.Rebuilds);
        window.Close();
    }
}
