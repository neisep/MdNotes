using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

/// <summary>A replaced preview tree must become garbage; anything else is a leak per navigation.</summary>
public class MarkdownViewLeakTests
{
    [AvaloniaTheory]
    [InlineData("plain", "# A\n\nSome **text** and a [[Link]].")]
    [InlineData("tasklist", "- [x] done\n- [ ] open")]
    [InlineData("mermaid", "```mermaid\nflowchart LR\n  A --> B\n```")]
    [InlineData("code", "```csharp\nvar x = 1;\n```")]
    [InlineData("table", "| a | b |\n|---|---|\n| 1 | 2 |")]
    public void Replaced_preview_tree_is_collected(string _, string markdown)
    {
        var view = new MarkdownView { DiagramRenderer = new MermaiderDiagramRenderer() };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();

        var old = RenderThenReplace(window, view, markdown);

        for (var i = 0; i < 3 && old.IsAlive; i++)
        {
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(old.IsAlive, "the previous preview tree is still reachable");
        window.Close();
    }

    // Separate frame so the JIT cannot keep the old tree alive through a local of the test method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RenderThenReplace(Window window, MarkdownView view, string markdown)
    {
        var renderer = new MarkdownRenderer();
        view.Document = renderer.Parse(markdown);
        window.CaptureRenderedFrame();
        var old = new WeakReference(view.Child!);
        view.Document = renderer.Parse("replaced");
        window.CaptureRenderedFrame();
        return old;
    }
}
