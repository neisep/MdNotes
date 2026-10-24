using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;
using MdNotes.Views;
using MdNotes.Views.Markdown;
using MdNotes.Views.Shell;
using PathShape = Avalonia.Controls.Shapes.Path;

namespace MdNotes.Tests;

public class MainWindowTests
{
    // The diagram renderer used to be a static the app wrote once, so nothing ever checked that a
    // second window could render at all - and nothing could release the first window's caches when
    // it closed. It arrives through the service set and the preview binding now, per window.
    [AvaloniaFact]
    public async Task Every_window_renders_diagrams_through_the_injected_renderer()
    {
        using var vault = new TempVault();
        vault.Note("Plan.md", "# Plan\n\n```mermaid\nflowchart LR\n  A --> B\n```\n");

        var renderer = new DiagramCacheTests.CountingRenderer();
        var services = TestServices.For(vault, new FakeDialogs()) with { Diagrams = renderer };

        var windows = new List<(MainWindow Window, MainViewModel Vm)>();
        for (var i = 0; i < 2; i++)
        {
            var vm = new MainViewModel(services);
            var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
            window.Show();
            Assert.True(await vm.TryOpenInitialVaultAsync());
            vm.Workspace.SelectedNode = vm.Workspace.Tree[0];
            window.CaptureRenderedFrame();
            windows.Add((window, vm));

            Assert.Single(window.GetVisualDescendants().OfType<DiagramView>());
            Assert.Same(renderer, window.GetVisualDescendants().OfType<MarkdownView>().Single().DiagramRenderer);
        }

        // Each window caches on its own, so the second renders the same diagram again rather than
        // reading a process-wide cache - the cost of the ownership the static did not have.
        Assert.Equal(2, renderer.Calls);

        foreach (var (window, vm) in windows)
        {
            vm.Dispose();
            window.Close();
        }
    }

    /// <summary>The ModalOverlay hosting the view model of the given type - one chrome, three contents.</summary>
    /// <summary>
    /// Finding 7 through the real window: Edit-only hides the preview, typing there builds nothing, and
    /// going back to Split shows what was typed. Pins the IsVisible binding on the MarkdownView itself,
    /// which is what the view watches - its container's alone would not tell it anything.
    /// </summary>
    [AvaloniaFact]
    public async Task Typing_in_edit_only_mode_builds_no_preview_and_split_shows_the_latest_text()
    {
        using var vault = new TempVault();
        vault.Note("Plan.md", "first");
        var vm = new MainViewModel(TestServices.For(vault, new FakeDialogs()));
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        Assert.True(await vm.TryOpenInitialVaultAsync());
        vm.Workspace.SelectedNode = vm.Workspace.Tree[0];
        window.CaptureRenderedFrame();
        var preview = window.GetVisualDescendants().OfType<MarkdownView>().Single();

        vm.ViewMode = ViewMode.Edit;
        var hidden = preview.Rebuilds;
        vm.Editor.Load(vault.Note("Plan.md", "typed while hidden"));
        window.CaptureRenderedFrame();
        Assert.Equal(hidden, preview.Rebuilds);

        vm.ViewMode = ViewMode.Split;
        window.CaptureRenderedFrame();

        Assert.Equal(hidden + 1, preview.Rebuilds);
        Assert.Contains(preview.GetVisualDescendants().OfType<TextBlock>(), t => t.Inlines?.Text == "typed while hidden");
        vm.Dispose();
        window.Close();
    }

    private static ModalOverlay Overlay<TViewModel>(Window window) =>
        window.GetVisualDescendants().OfType<ModalOverlay>().Single(o => o.DataContext is TViewModel);

    [AvaloniaFact]
    public async Task Opens_last_vault_shows_tree_and_renders_the_selected_note()
    {
        using var vault = new TempVault();
        vault.Note("Ideas/Plan.md", "# Plan\n\n```mermaid\nflowchart LR\n  A --> B\n```\n");
        vault.Note("Todo.md", "- [ ] one");

        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(TestServices.For(vault, dialogs));
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.True(await vm.TryOpenInitialVaultAsync());
        Assert.Equal(new[] { "Ideas", "Todo" }, vm.Workspace.Tree.Select(n => n.Name));

        // The sidebar binds the workspace, not the window. Its DataContext is set in MainWindow.axaml
        // and a wrong one is not a compile error - it would just draw an empty sidebar.
        Assert.IsType<VaultWorkspaceViewModel>(
            window.GetVisualDescendants().OfType<Sidebar>().Single().DataContext);

        vm.Workspace.SelectedNode = vm.Workspace.Tree[0].Children[0];
        window.CaptureRenderedFrame();

        Assert.Equal("Plan", System.IO.Path.GetFileNameWithoutExtension(vm.Editor.CurrentPath));
        var editor = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("editor"));
        Assert.StartsWith("# Plan", editor.Text);
        Assert.Contains(window.GetVisualDescendants().OfType<DiagramView>(), d => d.CurrentBitmap() is not null);
        Assert.Empty(dialogs.Errors);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Renders_shell_in_both_themes()
    {
        using var vault = new TempVault();
        vault.Note("Ideas/Plan.md",
            "# Plan\n\n" +
            "A paragraph with **bold**, a [[Wikilink]] and a [[Ideas/]] folder link.\n\n" +
            "- [x] done\n" +
            "- [ ] open\n\n" +
            "| Col1 | Col2 |\n" +
            "|------|------|\n" +
            "| a    | b    |\n\n" +
            "```csharp\n" +
            "var x = 1;\n" +
            "```\n\n" +
            "> A quote\n\n" +
            "```mermaid\n" +
            "flowchart LR\n" +
            "  A --> B\n" +
            "```\n");
        vault.Note("Todo.md", "- [ ] task");

        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(TestServices.For(vault, dialogs));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 850 };
        window.Show();

        Assert.True(await vm.TryOpenInitialVaultAsync());
        vm.Workspace.Tree[0].IsExpanded = true;
        vm.Workspace.SelectedNode = vm.Workspace.Tree[0].Children[0];
        window.CaptureRenderedFrame();

        var renderOut = Environment.GetEnvironmentVariable("MDNOTES_RENDER_OUT");
        var themes = new[] { ThemeVariant.Light, ThemeVariant.Dark };
        var themeNames = new[] { "light", "dark" };

        try
        {
            for (int i = 0; i < themes.Length; i++)
            {
                Application.Current!.RequestedThemeVariant = themes[i];
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);

                if (renderOut is { Length: > 0 })
                {
                    var filename = $"main-window-{themeNames[i]}.png";
                    var filepath = System.IO.Path.Combine(renderOut, filename);
                    frame.Save(filepath, new PngBitmapEncoderOptions());
                    Assert.True(System.IO.File.Exists(filepath));
                }
            }
        }
        finally
        {
            // Restore theme to default
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }

        var visuals = window.GetVisualDescendants().ToList();

        // Assert shell components exist
        Assert.NotNull(visuals.OfType<Ribbon>().FirstOrDefault());
        Assert.NotNull(visuals.OfType<Sidebar>().FirstOrDefault());
        Assert.NotNull(visuals.OfType<PaneHeader>().FirstOrDefault());
        Assert.NotNull(visuals.OfType<StatusBar>().FirstOrDefault());

        // Assert at least 6 Path icons
        var iconPaths = visuals.OfType<PathShape>().Where(p => p.Classes.Contains("icon")).ToList();
        Assert.True(iconPaths.Count >= 6, $"Expected at least 6 icon paths, found {iconPaths.Count}");

        // Assert StatusBar contains "words" text
        var statusBarTexts = visuals.OfType<StatusBar>()
            .SelectMany(sb => sb.GetVisualDescendants().OfType<TextBlock>())
            .Select(tb => tb.Text)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
        Assert.Contains(statusBarTexts, t => t!.Contains("words"));

        Assert.Empty(dialogs.Errors);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Quick_switcher_overlay_opens_and_closes()
    {
        using var vault = new TempVault();
        vault.Note("Ideas/Plan.md", "# Plan");
        vault.Note("Todo.md", "- [ ] task");

        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(TestServices.For(vault, dialogs));
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.True(await vm.TryOpenInitialVaultAsync());
        vm.Workspace.SelectedNode = vm.Workspace.Tree[0].Children[0];
        window.CaptureRenderedFrame();

        // Open quick switcher
        vm.Workspace.OpenQuickSwitcherCommand.Execute(null);
        window.CaptureRenderedFrame();

        // Assert QuickSwitcher is visible and has correct DataContext
        // Visibility belongs to the hosting ModalOverlay now; the switcher is the content inside it.
        var quickSwitcher = Overlay<QuickSwitcherViewModel>(window);
        Assert.True(quickSwitcher.IsVisible);
        Assert.Single(quickSwitcher.GetVisualDescendants().OfType<QuickSwitcher>());

        // Assert Results is non-empty
        Assert.NotEmpty(vm.Workspace.QuickSwitcher.Results);

        // Close quick switcher
        vm.Workspace.QuickSwitcher.CloseCommand.Execute(null);
        window.CaptureRenderedFrame();

        // Assert QuickSwitcher is hidden
        Assert.False(quickSwitcher.IsVisible);

        Assert.Empty(dialogs.Errors);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Search_replace_overlay_opens_and_closes()
    {
        using var vault = new TempVault();
        vault.Note("Ideas/Plan.md", "the widget and another widget");
        vault.Note("Todo.md", "- [ ] ship the widget");

        var dialogs = new FakeDialogs();
        using var vm = new MainViewModel(TestServices.For(vault, dialogs));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 850 };
        window.Show();

        Assert.True(await vm.TryOpenInitialVaultAsync());
        vm.Workspace.SelectedNode = vm.Workspace.Tree[1];
        vm.Workspace.SearchQuery = "widget";
        vm.Workspace.SearchReplace.Replacement = "gadget";
        vm.Workspace.OpenSearchReplaceCommand.Execute(null);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        if (Environment.GetEnvironmentVariable("MDNOTES_RENDER_OUT") is { Length: > 0 } renderOut)
        {
            var filepath = System.IO.Path.Combine(renderOut, "search-replace.png");
            frame.Save(filepath, new PngBitmapEncoderOptions());
            Assert.True(System.IO.File.Exists(filepath));
        }

        var overlay = Overlay<SearchReplaceViewModel>(window);
        Assert.True(overlay.IsVisible);
        Assert.Single(overlay.GetVisualDescendants().OfType<SearchReplace>());

        // Seeded from the sidebar query, so the overlay already shows what the user was looking at.
        Assert.Equal("widget", vm.Workspace.SearchReplace.Find);
        Assert.Equal(2, vm.Workspace.SearchReplace.Results.Count);
        Assert.Equal(3, vm.Workspace.SearchReplace.Results.Sum(r => r.Matches.Count));

        vm.Workspace.SearchReplace.CloseCommand.Execute(null);
        window.CaptureRenderedFrame();

        Assert.False(overlay.IsVisible);
        Assert.Empty(dialogs.Errors);
        window.Close();
    }

    /// <summary>
    /// Finding 13's other half: the overlay's note list is virtualised, so a vault-wide result set
    /// builds containers for the notes on screen, not for all of them - and every one is still listed,
    /// because Replace all acts on the whole list.
    /// </summary>
    [AvaloniaFact]
    public async Task The_replace_overlay_only_builds_rows_for_the_notes_on_screen()
    {
        using var vault = new TempVault();
        for (var i = 0; i < 300; i++) vault.Note($"n{i:D3}.md", "a widget here");

        using var vm = new MainViewModel(TestServices.For(vault, new FakeDialogs()));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 850 };
        window.Show();
        Assert.True(await vm.TryOpenInitialVaultAsync());

        await vm.Workspace.SearchReplace.OpenAsync("widget", null);
        window.CaptureRenderedFrame();

        var overlay = Overlay<SearchReplaceViewModel>(window);
        var list = overlay.GetVisualDescendants().OfType<ItemsControl>()
            .Single(i => ReferenceEquals(i.ItemsSource, vm.Workspace.SearchReplace.Results));
        int Realised() => list.ItemsPanelRoot!.Children.Count(c => c.IsVisible);

        Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
        Assert.Equal(300, vm.Workspace.SearchReplace.Results.Count);
        Assert.InRange(Realised(), 1, 50);

        // Scrolled to the end, the last note is there to be seen - virtualised, not truncated.
        var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        window.CaptureRenderedFrame();
        Assert.Contains(list.ItemsPanelRoot!.Children,
            c => c.IsVisible && c.DataContext is NoteResultViewModel { Name: "n299" });
        Assert.InRange(Realised(), 1, 50);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Settings_overlay_opens_and_closes()
    {
        using var vault = new TempVault();
        vault.Note("Todo.md", "- [ ] ship the widget");

        var dialogs = new FakeDialogs();
        var settings = vault.Settings;
        using var vm = new MainViewModel(TestServices.For(vault, dialogs));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 850 };
        window.Show();

        Assert.True(await vm.TryOpenInitialVaultAsync());
        vm.OpenSettingsCommand.Execute(null);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        if (Environment.GetEnvironmentVariable("MDNOTES_RENDER_OUT") is { Length: > 0 } renderOut)
        {
            var filepath = System.IO.Path.Combine(renderOut, "settings.png");
            frame.Save(filepath, new PngBitmapEncoderOptions());
            Assert.True(System.IO.File.Exists(filepath));
        }

        var overlay = Overlay<SettingsViewModel>(window);
        Assert.True(overlay.IsVisible);
        Assert.Single(overlay.GetVisualDescendants().OfType<SettingsPanel>());

        // The New notes section: the template path is typed into the panel.
        var templateBox = overlay.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "TemplateBox");
        Assert.NotNull(templateBox);
        Assert.Equal("New notes start blank.", vm.Settings.TemplateStatus);

        // The Excluded files section: a multi-line list, empty until someone writes one.
        var excludeBox = overlay.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "ExcludeBox");
        Assert.True(excludeBox.AcceptsReturn);
        Assert.Equal("Nothing is excluded.", vm.Settings.ExcludeStatus);

        // Picking a mode from the panel reaches the editor, which is what decides when to write.
        vm.Settings.SetSaveModeCommand.Execute(AutoSaveMode.Manual);
        Assert.Equal(AutoSaveMode.Manual, settings.AutoSave);
        Assert.Equal(AutoSaveMode.Manual, vm.Editor.SaveMode);

        vm.Settings.CloseCommand.Execute(null);
        window.CaptureRenderedFrame();

        Assert.False(overlay.IsVisible);
        Assert.Empty(dialogs.Errors);
        window.Close();
    }

}
