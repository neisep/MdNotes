using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

/// <summary>
/// Finding 12: an edited diagram renders off the UI thread, and the previous picture stays on screen
/// until it lands. A note's first showing is still synchronous - the existing view tests pin that.
/// </summary>
public class DiagramBackgroundRenderTests
{
    private const string First = "flowchart LR\n  A --> B";
    private const string Edited = "flowchart LR\n  A --> C";
    private const string TypedOn = "flowchart LR\n  A --> D";

    /// <summary>The real renderer, held at a gate for chosen sources, noting which thread asked.</summary>
    private sealed class GatedRenderer : IDiagramRenderer
    {
        private readonly MermaiderDiagramRenderer _inner = new();
        public readonly SemaphoreSlim Gate = new(0);
        public readonly HashSet<string> Held = [];
        public readonly List<(string Source, bool OnUiThread)> Calls = [];

        public string RenderSvg(string mermaidSource, DiagramPalette palette)
        {
            lock (Calls) Calls.Add((mermaidSource, Dispatcher.UIThread.CheckAccess()));
            if (Held.Contains(mermaidSource)) Gate.Wait(TimeSpan.FromSeconds(10));
            return _inner.RenderSvg(mermaidSource, palette);
        }
    }

    private static string Fence(string source) => $"# Plan\n\n```mermaid\n{source}\n```\n";

    private static async Task Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "timed out waiting for the background render");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
    }

    private static (Window, MarkdownView) Show(GatedRenderer renderer)
    {
        var view = new MarkdownView { DiagramRenderer = renderer, Document = new MarkdownRenderer().Parse(Fence(First)) };
        var window = new Window { Width = 900, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static DiagramView Diagram(MarkdownView view) =>
        Assert.Single(view.GetVisualDescendants().OfType<DiagramView>());

    [AvaloniaFact]
    public async Task An_edit_renders_off_the_UI_thread_and_shows_the_previous_picture_meanwhile()
    {
        var renderer = new GatedRenderer { Held = { Edited } };
        var (window, view) = Show(renderer);
        Assert.Equal(First, Diagram(view).Source);

        view.Document = new MarkdownRenderer().Parse(Fence(Edited));
        Dispatcher.UIThread.RunJobs();

        // Rebuilt, still showing the first picture: the edit is rendering behind it.
        Assert.Equal(First, Diagram(view).Source);
        await Until(() => renderer.Calls.Any(c => c.Source == Edited));
        Assert.False(renderer.Calls.Single(c => c.Source == Edited).OnUiThread);

        renderer.Gate.Release();
        await Until(() => Diagram(view).Source == Edited);
        Assert.NotNull(Diagram(view).CurrentBitmap());
        window.Close();
    }

    /// <summary>
    /// A render the user typed past is let go rather than cached - the half-typed states finding 12 says
    /// were being held - and only the one still wanted is shown.
    /// </summary>
    [AvaloniaFact]
    public async Task A_render_typed_past_is_not_kept()
    {
        var renderer = new GatedRenderer { Held = { Edited } };
        var (window, view) = Show(renderer);

        view.Document = new MarkdownRenderer().Parse(Fence(Edited));
        Dispatcher.UIThread.RunJobs();
        await Until(() => renderer.Calls.Any(c => c.Source == Edited));

        // Typed on while the edit is still rendering. Renders take turns, so the newer one waits for it.
        view.Document = new MarkdownRenderer().Parse(Fence(TypedOn));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(First, Diagram(view).Source);

        renderer.Gate.Release();
        await Until(() => Diagram(view).Source == TypedOn);

        var cache = view.DiagramCacheInUse!;
        Assert.Equal(TypedOn, Diagram(view).Source);
        Assert.DoesNotContain(renderer.Calls, c => c.Source == Edited && c.OnUiThread);
        Assert.Equal(2, cache.PictureCount); // the first and the latest; the one typed past was disposed
        window.Close();
    }

    /// <summary>
    /// A broken edit lands as the error, once: the failure is remembered, so the rebuild that shows it
    /// does not render it again - which would land, rebuild, and render again, forever.
    /// </summary>
    [AvaloniaFact]
    public async Task A_broken_edit_shows_its_error_and_is_rendered_once()
    {
        const string broken = "notADiagram\n  foo --> bar";
        var renderer = new GatedRenderer();
        var (window, view) = Show(renderer);

        view.Document = new MarkdownRenderer().Parse(Fence(broken));
        await Until(() => view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Mermaid: ") == true));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(renderer.Calls, c => c.Source == broken);
        Assert.Empty(view.GetVisualDescendants().OfType<DiagramView>());
        window.Close();
    }
}
