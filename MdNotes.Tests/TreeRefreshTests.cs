using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MdNotes.Models;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// The refresh contract: the tree is reconciled against the vault, never replaced. A row that is
/// still in the vault is the same object afterwards, so whatever the sidebar hung on it - expansion
/// today, the next thing tomorrow - survives without RefreshTree remembering any of it.
///
/// The first half drives <see cref="TreeNodeViewModel.Reconcile"/> directly, which needs no vault and
/// no UI thread. The second half goes through MainViewModel over a real vault, where the 300 ms
/// debounce has to be waited out - AvaloniaFact there, because the view models own DispatcherTimers.
/// </summary>
public class TreeRefreshTests
{
    // ---- the reconcile itself ----------------------------------------------------------------

    [Fact]
    public void An_unchanged_read_touches_nothing()
    {
        var entries = Entries("Box/", "a.md", "b.md");
        var rows = Rows(entries);
        var box = rows[0];
        var changes = Record(rows);

        TreeNodeViewModel.Reconcile(rows, Entries("Box/", "a.md", "b.md"));

        Assert.Empty(changes);
        Assert.Same(box, rows[0]);
    }

    [Fact]
    public void A_new_entry_is_inserted_in_place_and_leaves_the_others_alone()
    {
        var rows = Rows(Entries("a.md", "c.md"));
        var (a, c) = (rows[0], rows[1]);

        TreeNodeViewModel.Reconcile(rows, Entries("a.md", "b.md", "c.md"));

        Assert.Equal(["a", "b", "c"], rows.Select(r => r.Name));
        Assert.Same(a, rows[0]);
        Assert.Same(c, rows[2]);
    }

    [Fact]
    public void A_removed_entry_takes_only_its_own_row()
    {
        var rows = Rows(Entries("a.md", "b.md", "c.md"));
        var (a, c) = (rows[0], rows[2]);

        TreeNodeViewModel.Reconcile(rows, Entries("a.md", "c.md"));

        Assert.Equal(["a", "c"], rows.Select(r => r.Name));
        Assert.Same(a, rows[0]);
        Assert.Same(c, rows[1]);
    }

    /// <summary>What makes a '.order' change cost the sidebar nothing: the row is moved, not rebuilt.</summary>
    [Fact]
    public void A_reorder_moves_the_row_rather_than_rebuilding_it()
    {
        var rows = Rows(Entries("a.md", "b.md", "c.md"));
        var a = rows[0];
        var changes = Record(rows);

        TreeNodeViewModel.Reconcile(rows, Entries("b.md", "a.md", "c.md"));

        Assert.Equal([NotifyCollectionChangedAction.Move], changes);
        Assert.Same(a, rows[1]);
    }

    [Fact]
    public void View_state_on_a_surviving_row_is_never_touched()
    {
        var rows = Rows(Entries("Box/", "a.md"));
        rows[0].IsExpanded = true;

        TreeNodeViewModel.Reconcile(rows, Entries("Box/", "a.md", "b.md"));

        Assert.True(rows[0].IsExpanded);
    }

    [Fact]
    public void A_surviving_folder_keeps_its_identity_while_its_children_change()
    {
        var rows = Rows([Folder("Box", "x.md")]);
        var box = rows[0];
        var x = box.Children[0];

        TreeNodeViewModel.Reconcile(rows, [Folder("Box", "x.md", "y.md")]);

        Assert.Same(box, rows[0]);
        Assert.Same(x, box.Children[0]);
        Assert.Equal(["x", "y"], box.Children.Select(c => c.Name));
    }

    /// <summary>
    /// The folder flag is half the identity: a note deleted and a folder made under the same name is
    /// a different row, not the same row lying about which of the two it is.
    /// </summary>
    [Fact]
    public void A_path_that_stopped_being_a_folder_is_a_different_row()
    {
        var rows = Rows([new VaultEntry("Box", Path.Combine(Root, "Box"), true, [])]);
        var box = rows[0];

        TreeNodeViewModel.Reconcile(rows, [new VaultEntry("Box", Path.Combine(Root, "Box"), false, [])]);

        Assert.NotSame(box, rows[0]);
        Assert.False(rows[0].IsFolder);
    }

    /// <summary>
    /// Case is part of the path here, unlike in FindNode: 'Plan.md' renamed to 'plan.md' is a rename,
    /// and a row that kept the old spelling would send the editor to a file that is not there.
    /// </summary>
    [Fact]
    public void A_case_only_rename_is_a_different_row()
    {
        var rows = Rows(Entries("Plan.md"));
        var before = rows[0];

        TreeNodeViewModel.Reconcile(rows, Entries("plan.md"));

        Assert.NotSame(before, rows[0]);
        Assert.Equal("plan", rows[0].Name);
    }

    /// <summary>
    /// Finding 6: a row knows its folder, so siblings and ancestors need no search - including a row a
    /// later reconcile adds into a folder that was already there.
    /// </summary>
    [Fact]
    public void Every_row_knows_the_folder_it_sits_in()
    {
        var rows = Rows([Folder("Box", "x.md"), .. Entries("a.md")]);
        var box = rows[0];

        TreeNodeViewModel.Reconcile(rows, [Folder("Box", "x.md", "y.md"), .. Entries("a.md")]);

        Assert.Null(box.Parent);
        Assert.Null(rows[1].Parent);
        Assert.All(box.Children, child => Assert.Same(box, child.Parent));
        Assert.Equal(["x", "y"], box.Children.Select(c => c.Name));
    }

    /// <summary>The explicit-stack walk hands rows out in the order the sidebar draws them.</summary>
    [Fact]
    public void The_walk_visits_rows_top_to_bottom()
    {
        var rows = Rows([Folder("A", "a1.md", "a2.md"), Folder("B", "b1.md"), .. Entries("c.md")]);

        Assert.Equal(["A", "a1", "a2", "B", "b1", "c"], TreeNodeViewModel.Walk(rows).Select(r => r.Name));
        Assert.Equal(["B", "b1"], rows[1].SelfAndDescendants().Select(r => r.Name));
    }

    // ---- end to end, over a real vault --------------------------------------------------------

    /// <summary>The regression the whole thing exists for: someone else's file change must cost nothing.</summary>
    [AvaloniaFact]
    public async Task An_unrelated_file_appearing_leaves_expansion_and_identity_alone()
    {
        using var vault = new TempVault();
        vault.Note("Box/x.md");
        vault.Note("a.md");
        using var vm = await OpenAsync(vault);

        var box = vm.Workspace.Tree.Single(n => n.Name == "Box");
        box.IsExpanded = true;

        // Written behind the app's back, as an external editor would: the real watcher raises it.
        vault.Note("b.md");
        await SettleAsync();

        Assert.Equal(["Box", "a", "b"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.Same(box, vm.Workspace.Tree.Single(n => n.Name == "Box"));
        Assert.True(box.IsExpanded);
    }

    /// <summary>The same guarantee without the watcher, so it is pinned by something that cannot flake.</summary>
    [AvaloniaFact]
    public async Task Creating_a_note_from_the_app_leaves_the_other_rows_alone()
    {
        using var vault = new TempVault();
        vault.Note("Box/x.md");
        using var vm = await OpenAsync(vault);

        var box = vm.Workspace.Tree.Single(n => n.Name == "Box");
        box.IsExpanded = true;

        await vm.Workspace.NewNoteCommand.ExecuteAsync(null);

        Assert.Same(box, vm.Workspace.Tree.Single(n => n.Name == "Box"));
        Assert.True(box.IsExpanded);
        Assert.Contains(vm.Workspace.Tree, n => n.Name == "Untitled");
    }

    /// <summary>
    /// The one thing RefreshTree still restores by hand. Selecting a folder leaves the editor alone,
    /// so deleting that folder takes the selection away while a note is still open elsewhere.
    /// </summary>
    [AvaloniaFact]
    public async Task Deleting_the_selected_folder_falls_back_to_the_open_note()
    {
        using var vault = new TempVault();
        vault.Note("Keep.md");
        vault.Note("Box/x.md");
        using var vm = await OpenAsync(vault);

        var keep = vm.Workspace.Tree.Single(n => n.Name == "Keep");
        vm.Workspace.SelectedNode = keep;
        Assert.Equal(keep.FullPath, vm.Editor.CurrentPath);

        vm.Workspace.SelectedNode = vm.Workspace.Tree.Single(n => n.Name == "Box");
        await vm.Workspace.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["Keep"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.Same(keep, vm.Workspace.SelectedNode);
    }

    [AvaloniaFact]
    public async Task A_deleted_folder_takes_its_children_out_of_the_tree()
    {
        using var vault = new TempVault();
        vault.Note("Box/x.md");
        vault.Note("a.md");
        using var vm = await OpenAsync(vault);

        vm.Workspace.SelectedNode = vm.Workspace.Tree.Single(n => n.Name == "Box");
        await vm.Workspace.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["a"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.Null(vm.Workspace.FindNode(Path.Combine(vault.Root, "Box", "x.md")));
    }

    /// <summary>
    /// The path index behind FindNode is rebuilt on every reconcile: a row the app just made is found,
    /// by any casing of its path as the tree walk it replaced allowed, and it knows its folder.
    /// </summary>
    [AvaloniaFact]
    public async Task A_new_row_is_found_by_path_in_any_case_and_knows_its_folder()
    {
        using var vault = new TempVault();
        vault.Note("Box/x.md");
        using var vm = await OpenAsync(vault);
        var box = vm.Workspace.Tree.Single(n => n.Name == "Box");
        vm.Workspace.SelectedNode = box;

        await vm.Workspace.NewNoteCommand.ExecuteAsync(null);

        var made = vm.Workspace.FindNode(Path.Combine(vault.Root, "Box", "Untitled.md").ToUpperInvariant());
        Assert.NotNull(made);
        Assert.Same(box, made.Parent);
        Assert.Same(made, vm.Workspace.SelectedNode);
        Assert.True(box.IsExpanded);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mdnotes-reconcile");

    /// <summary>Entry list from names; a trailing '/' makes a folder, anything else a note.</summary>
    private static IReadOnlyList<VaultEntry> Entries(params string[] names) =>
        [.. names.Select(n => n.EndsWith('/')
            ? new VaultEntry(n[..^1], Path.Combine(Root, n[..^1]), true, [])
            : new VaultEntry(Path.GetFileNameWithoutExtension(n), Path.Combine(Root, n), false, []))];

    private static VaultEntry Folder(string name, params string[] children)
    {
        var path = Path.Combine(Root, name);
        return new VaultEntry(name, path, true,
            [.. children.Select(c => new VaultEntry(
                Path.GetFileNameWithoutExtension(c), Path.Combine(path, c), false, []))]);
    }

    private static ObservableCollection<TreeNodeViewModel> Rows(IReadOnlyList<VaultEntry> entries)
    {
        var rows = new ObservableCollection<TreeNodeViewModel>();
        TreeNodeViewModel.Reconcile(rows, entries);
        return rows;
    }

    /// <summary>The collection changes the reconcile causes, which is where 'moved, not rebuilt' shows.</summary>
    private static List<NotifyCollectionChangedAction> Record(ObservableCollection<TreeNodeViewModel> rows)
    {
        List<NotifyCollectionChangedAction> changes = [];
        rows.CollectionChanged += (_, e) => changes.Add(e.Action);
        return changes;
    }

    /// <summary>The refresh the vault asks for is debounced, so it has to be waited out.</summary>
    private static async Task SettleAsync()
    {
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<MainViewModel> OpenAsync(TempVault vault)
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(TestServices.For(vault, dialogs));

        Assert.True(await vm.TryOpenInitialVaultAsync());
        Assert.Empty(dialogs.Errors);
        return vm;
    }
}
