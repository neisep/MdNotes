using Avalonia.Headless.XUnit;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// Two windows over the one shared vault - the arrangement nothing else in the suite builds, and the
/// one the vault was not originally designed for. Switching the vault in one window used to leave the
/// other naming the old vault while resolving every path against the new one.
/// AvaloniaFact throughout: MainViewModel owns DispatcherTimers, so it needs a UI thread.
/// </summary>
public sealed class MultiWindowVaultTests : IDisposable
{
    private readonly TempVault _first = new();
    private readonly string _secondRoot = Directory.CreateTempSubdirectory("mdnotes-second-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly List<MainViewModel> _windows = [];

    /// <summary>
    /// One service set for every window, as AppSession hands it out - a window with its own copy of the
    /// settings could not disagree with another about the theme, because it would never see it.
    /// </summary>
    private readonly AppServices _services;

    public MultiWindowVaultTests()
    {
        _services = TestServices.For(_first, _dialogs);
        _first.Note("Alpha.md", "# Alpha");
        File.WriteAllText(Path.Combine(_secondRoot, "Beta.md"), "# Beta");
        _first.Settings.LastVaultPath = _first.Root;
    }

    public void Dispose()
    {
        foreach (var window in _windows) window.Dispose();
        _first.Dispose();
        Directory.Delete(_secondRoot, recursive: true);
    }

    /// <summary>
    /// A window on the shared vault, wired the way AppSession wires one. Awaits the first tree walk,
    /// which now runs on the thread pool - a window that has restored the vault has not yet drawn it.
    /// </summary>
    private async Task<MainViewModel> NewWindowAsync()
    {
        var vm = new MainViewModel(_services);
        _windows.Add(vm);
        vm.TryRestoreVault();
        await vm.Workspace.TreeReady;
        return vm;
    }

    /// <summary>Switches the vault the way the user does: the Ctrl+Shift+O command, through the picker.</summary>
    private async Task OpenVaultAsync(MainViewModel window, string root)
    {
        _dialogs.PickFolderResult = root;
        await window.Workspace.OpenVaultCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task A_second_window_joining_an_open_vault_gets_the_tree()
    {
        var a = await NewWindowAsync();
        var b = await NewWindowAsync();

        // Open() is a no-op for the vault already open, so B has to populate itself.
        Assert.Equal(a.Workspace.Tree.Select(n => n.Name), b.Workspace.Tree.Select(n => n.Name));
        Assert.Contains(b.Workspace.Tree, n => n.Name == "Alpha");
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Switching_the_vault_in_one_window_renames_and_rebuilds_the_other()
    {
        var a = await NewWindowAsync();
        var b = await NewWindowAsync();
        var c = await NewWindowAsync();
        b.Workspace.SelectedNode = b.Workspace.Tree.First(n => n.Name == "Alpha");
        b.Workspace.SearchQuery = "alpha";

        await OpenVaultAsync(a, _secondRoot);
        await b.Workspace.TreeReady;
        await c.Workspace.TreeReady;

        Assert.Equal(Path.GetFileName(_secondRoot), b.Workspace.VaultName);
        Assert.Equal(_secondRoot, b.Workspace.VaultPath);
        Assert.Equal(["Beta"], b.Workspace.Tree.Select(n => n.Name));
        Assert.Equal(string.Empty, b.Workspace.SearchQuery);
        Assert.Null(b.Workspace.SelectedNode);

        // The title names the open note when there is one, so the vault half of it shows in a window
        // holding nothing - which is exactly the window that used to keep naming the old vault.
        Assert.Equal($"MdNotes - {Path.GetFileName(_secondRoot)}", c.WindowTitle);
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task A_closed_window_stops_following_the_vault()
    {
        var a = await NewWindowAsync();
        var closed = await NewWindowAsync();
        var before = closed.Workspace.Tree.Select(n => n.Name).ToArray();

        closed.Dispose();
        await OpenVaultAsync(a, _secondRoot);

        // The vault is process-wide, so a window that has let go must not be rebuilt by it. Nothing
        // asserted this before: the tree, the title and the timers all detach in one method, and a
        // handler left behind would only show up as a window rebuilding itself after it was gone.
        Assert.Equal(before, closed.Workspace.Tree.Select(n => n.Name));
        Assert.Equal(_secondRoot, a.Workspace.VaultPath);

        // VaultPath is not asserted on the closed window: it reads the shared service rather than
        // keeping a copy, so it answers for whatever vault is open now. The tree is the thing the
        // handler rebuilds, and the tree is what must have stood still.
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task A_note_left_open_in_the_other_window_becomes_loose_and_says_so()
    {
        var a = await NewWindowAsync();
        var b = await NewWindowAsync();
        b.Workspace.SelectedNode = b.Workspace.Tree.First(n => n.Name == "Alpha");
        Assert.False(b.Editor.IsLooseFile);
        Assert.Equal(AutoSaveMode.AfterDelay, b.Editor.SaveMode);

        var changed = new List<string>();
        b.Editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        await OpenVaultAsync(a, _secondRoot);

        // The buffer survives - closing another window's note would lose work - but it is now outside
        // the vault, so it saves manually, and the status bar is told rather than left stale.
        Assert.Equal("Alpha", b.Editor.NoteName);
        Assert.True(b.Editor.IsLooseFile);
        Assert.Equal(AutoSaveMode.Manual, b.Editor.SaveMode);
        Assert.Contains(nameof(EditorViewModel.IsLooseFile), changed);
        Assert.Contains(nameof(EditorViewModel.SaveStatus), changed);
    }

    [AvaloniaFact]
    public async Task Cycling_the_theme_in_one_window_moves_every_window()
    {
        var a = await NewWindowAsync();
        var b = await NewWindowAsync();
        var changed = new List<string>();
        b.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        a.CycleThemeCommand.Execute(null);

        // One variant is applied to the whole application and one value is persisted, so a window
        // holding its own copy would show a theme the app is not using.
        Assert.Equal(ThemeMode.Light, a.ThemeMode);
        Assert.Equal(ThemeMode.Light, b.ThemeMode);
        Assert.Contains(nameof(MainViewModel.ThemeMode), changed);
    }

    [AvaloniaFact]
    public async Task Commands_gated_on_having_a_vault_stay_enabled_in_the_other_window()
    {
        var a = await NewWindowAsync();
        var b = await NewWindowAsync();

        await OpenVaultAsync(a, _secondRoot);

        Assert.True(b.Workspace.HasVault);
        Assert.True(b.Workspace.NewNoteCommand.CanExecute(null));
        Assert.True(b.Workspace.OpenSearchReplaceCommand.CanExecute(null));
    }
}
