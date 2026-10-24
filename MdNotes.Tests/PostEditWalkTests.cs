using Avalonia.Headless.XUnit;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// Finding 21: the tree walk after the user changes one entry runs on the UI thread because it is
/// meant to be cheap, and it is cheap only while the content cache holds every note's head. Past the
/// budget it re-reads the vault, so it has to go to the thread pool - and the selection that follows it
/// has to wait for it rather than look for a row that is not there yet.
/// </summary>
public class PostEditWalkTests
{
    private const int NoteChars = 10;

    /// <summary>Three notes against room for two: any walk that reads them all overruns.</summary>
    private static TempVault OverBudget(bool titles)
    {
        var vault = new TempVault(contentBudgetChars: 2 * NoteChars);
        vault.Settings.UseNoteTitles = titles;
        foreach (var name in new[] { "a", "b", "c" })
            vault.Note($"{name}.md", new string(name[0], NoteChars));
        return vault;
    }

    [Fact]
    public void A_walk_stops_being_cheap_once_reading_titles_overruns_the_cache()
    {
        using var vault = OverBudget(titles: true);
        Assert.True(vault.Service.TreeWalkIsCheap);

        vault.Service.GetTree();

        Assert.False(vault.Service.TreeWalkIsCheap);
    }

    /// <summary>With titles off the walk opens no note, so the cache's size has nothing to do with it.</summary>
    [Fact]
    public void A_walk_without_titles_stays_cheap_however_full_the_cache_is()
    {
        using var vault = OverBudget(titles: false);
        foreach (var note in vault.Service.GetNotes()) vault.Service.Read(note);

        Assert.True(vault.Service.TreeWalkIsCheap);
    }

    [AvaloniaFact]
    public async Task Over_budget_a_move_walks_off_the_UI_thread_and_then_selects_the_moved_row()
    {
        using var vault = OverBudget(titles: true);
        using var vm = await OpenAsync(vault);
        Assert.False(vault.Service.TreeWalkIsCheap);

        vm.Workspace.SelectedNode = Node(vm, "a");
        var moving = vm.Workspace.MoveDownCommand.ExecuteAsync(null);

        // Not finished on return: the walk went to the thread pool, and its answer needs this thread.
        Assert.False(moving.IsCompleted);
        await moving;

        Assert.Equal(["b", "a", "c"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.Equal("a", vm.Workspace.SelectedNode?.Name);
    }

    /// <summary>
    /// Under budget, but the vault's first walk has not finished: the cache has not read the notes yet,
    /// so a walk here would read them all on the UI thread. The walk's answer needs this thread, so it
    /// cannot finish before the test yields - which is what makes the in-flight state deterministic.
    /// </summary>
    [AvaloniaFact]
    public async Task During_the_first_walk_a_new_note_walks_off_the_UI_thread()
    {
        using var vault = new TempVault();
        vault.Settings.UseNoteTitles = true;
        vault.Note("a.md", "# A");
        using var vm = new MainViewModel(TestServices.For(vault, new FakeDialogs()));

        var opening = vm.TryOpenInitialVaultAsync();
        Assert.False(vm.Workspace.TreeReady.IsCompleted);

        var creating = vm.Workspace.NewNoteCommand.ExecuteAsync(null);

        Assert.False(creating.IsCompleted);
        Assert.True(await opening);
        await creating;

        Assert.Equal(["a", "Untitled"], vm.Workspace.Tree.Select(n => n.Name).Order());
        Assert.Equal("Untitled", vm.Workspace.SelectedNode?.Name);
    }

    /// <summary>The common case keeps what it had: done before the command returns, no dispatcher hop.</summary>
    [AvaloniaFact]
    public async Task Under_budget_a_move_finishes_before_the_command_returns()
    {
        using var vault = new TempVault();
        vault.Settings.UseNoteTitles = true;
        vault.Note("a.md", "# A");
        vault.Note("b.md", "# B");
        using var vm = await OpenAsync(vault);

        vm.Workspace.SelectedNode = Node(vm, "a");
        var moving = vm.Workspace.MoveDownCommand.ExecuteAsync(null);

        Assert.True(moving.IsCompleted);
        Assert.Equal(["b", "a"], vm.Workspace.Tree.Select(n => n.Name));
    }

    private static TreeNodeViewModel Node(MainViewModel vm, string name) =>
        vm.Workspace.Tree.First(n => n.Name == name);

    private static async Task<MainViewModel> OpenAsync(TempVault vault)
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(TestServices.For(vault, dialogs));

        Assert.True(await vm.TryOpenInitialVaultAsync());
        Assert.Empty(dialogs.Errors);
        return vm;
    }
}
