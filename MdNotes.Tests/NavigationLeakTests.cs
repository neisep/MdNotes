using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MdNotes.Services;
using MdNotes.ViewModels;
using MdNotes.Views;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

/// <summary>Navigating between notes must not retain old preview trees or re-render unchanged diagrams.</summary>
public class NavigationLeakTests
{
    private const string NoteA = "# A\n\nText with **bold** and [[B]].\n\n- [x] done\n\n```mermaid\nflowchart LR\n  A --> B\n```\n\n```mermaid\nsequenceDiagram\n  Alice->>Bob: Hello\n```\n";
    private const string NoteB = "# B\n\nBack to [[A]].\n\n| a | b |\n|---|---|\n| 1 | 2 |\n";

    [AvaloniaFact]
    public async Task Navigating_between_notes_releases_old_trees_and_reuses_diagrams()
    {
        using var vault = new TempVault();
        vault.Note("A.md", NoteA);
        vault.Note("Folder/B.md", NoteB);

        var renderer = new DiagramCacheTests.CountingRenderer();
        using var vm = new MainViewModel(TestServices.For(vault, new ThrowingDialogs()) with { Diagrams = renderer });
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 850 };
        window.Show();
        Assert.True(await vm.TryOpenInitialVaultAsync());

        var oldPreview = NavigateAndCapture(vm, window, 20);
        for (var i = 0; i < 3 && oldPreview.IsAlive; i++)
        {
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(oldPreview.IsAlive, "a replaced preview tree is still reachable");
        Assert.Equal(2, renderer.Calls); // two diagrams in A, rendered once each across 10 visits
        Assert.Single(window.GetVisualDescendants().OfType<MarkdownView>());
        window.Close();
    }

    // The sidebar tree is deliberately not weighed here: it is reconciled rather than rebuilt, so a
    // row still in the vault is meant to be the same object afterwards. TreeRefreshTests asserts that
    // from the other side.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference NavigateAndCapture(MainViewModel vm, Window window, int times)
    {
        vm.Workspace.OpenWikiLinkCommand.Execute("A");
        window.CaptureRenderedFrame();
        var view = window.GetVisualDescendants().OfType<MarkdownView>().Single();
        var preview = new WeakReference(view.Child!);

        for (var i = 0; i < times; i++)
        {
            vm.Workspace.OpenWikiLinkCommand.Execute(i % 2 == 0 ? "B" : "A");
            window.CaptureRenderedFrame();
        }
        return preview;
    }
}
