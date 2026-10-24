using Avalonia.Headless.XUnit;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>Manual sidebar order: the move commands and the drag index maths.</summary>
public class TreeOrderTests
{
    [AvaloniaFact]
    public async Task MoveDown_reorders_the_tree_and_keeps_the_node_selected()
    {
        using var vault = new TempVault();
        vault.Note("a.md");
        vault.Note("b.md");
        vault.Note("c.md");
        using var vm = await OpenAsync(vault);

        var a = Node(vm, "a");
        vm.Workspace.SelectedNode = a;
        vm.Workspace.MoveDownCommand.Execute(null);

        Assert.Equal(["b", "a", "c"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.Equal("a", vm.Workspace.SelectedNode!.Name);

        // Moved, not rebuilt: the row is the same object, which is what lets it keep its own state.
        Assert.Same(a, vm.Workspace.Tree[1]);
        Assert.Same(a, vm.Workspace.SelectedNode);
    }

    [AvaloniaFact]
    public async Task MoveUp_on_the_first_item_changes_nothing()
    {
        using var vault = new TempVault();
        vault.Note("a.md");
        vault.Note("b.md");
        using var vm = await OpenAsync(vault);

        vm.Workspace.SelectedNode = Node(vm, "a");
        vm.Workspace.MoveUpCommand.Execute(null);

        Assert.Equal(["a", "b"], vm.Workspace.Tree.Select(n => n.Name));
        Assert.False(File.Exists(Path.Combine(vault.Root, ".order")));
    }

    [AvaloniaFact]
    public async Task ResetOrder_puts_the_selected_nodes_folder_back_to_alphabetical()
    {
        using var vault = new TempVault();
        vault.Note("a.md");
        vault.Note("b.md");
        using var vm = await OpenAsync(vault);

        vm.Workspace.SelectedNode = Node(vm, "b");
        vm.Workspace.MoveUpCommand.Execute(null);
        Assert.Equal(["b", "a"], vm.Workspace.Tree.Select(n => n.Name));

        vm.Workspace.ResetOrderCommand.Execute(null);

        Assert.Equal(["a", "b"], vm.Workspace.Tree.Select(n => n.Name));
    }

    /// <summary>
    /// The rule that used to live in the sidebar's hit test, where only a pointer-driven UI test could
    /// reach it. It needs neither a vault nor a window now.
    /// </summary>
    [Fact]
    public void PositionIn_reads_the_pointer_fraction_down_a_row()
    {
        var folder = new TreeNodeViewModel("Box", "/v/Box", isFolder: true);
        var note = new TreeNodeViewModel("a", "/v/a.md", isFolder: false);

        // A folder takes a drop into it across its middle half, and beside it at either end.
        Assert.Equal(DropPosition.Before, VaultWorkspaceViewModel.PositionIn(folder, 0.1));
        Assert.Equal(DropPosition.Into, VaultWorkspaceViewModel.PositionIn(folder, 0.5));
        Assert.Equal(DropPosition.After, VaultWorkspaceViewModel.PositionIn(folder, 0.9));

        // The band is open at both ends, so its edges still order rather than nest.
        Assert.Equal(DropPosition.Before, VaultWorkspaceViewModel.PositionIn(folder, 0.25));
        Assert.Equal(DropPosition.After, VaultWorkspaceViewModel.PositionIn(folder, 0.75));

        // A note has no inside, so it splits at its midpoint the whole way down.
        Assert.Equal(DropPosition.Before, VaultWorkspaceViewModel.PositionIn(note, 0.49));
        Assert.Equal(DropPosition.After, VaultWorkspaceViewModel.PositionIn(note, 0.5));
    }

    [AvaloniaFact]
    public async Task ResolveDrop_maps_before_after_and_into_to_a_folder_and_index()
    {
        using var vault = BoxVault();
        using var vm = await OpenAsync(vault);
        var box = Node(vm, "Box");
        var a = Node(vm, "a");
        var b = Node(vm, "b");

        // Tree is Box, a, b.
        Assert.Equal((vm.Workspace.VaultPath!, 0), vm.Workspace.ResolveDrop(b, box, DropPosition.Before));
        Assert.Equal((vm.Workspace.VaultPath!, 2), vm.Workspace.ResolveDrop(a, b, DropPosition.After));
        Assert.Equal((box.FullPath, 1), vm.Workspace.ResolveDrop(a, box, DropPosition.Into));
    }

    [AvaloniaFact]
    public async Task ResolveDrop_rejects_drops_that_cannot_or_need_not_happen()
    {
        using var vault = BoxVault();
        using var vm = await OpenAsync(vault);
        var box = Node(vm, "Box");
        var a = Node(vm, "a");
        var b = Node(vm, "b");

        Assert.Null(vm.Workspace.ResolveDrop(a, a, DropPosition.Before));                  // onto itself
        Assert.Null(vm.Workspace.ResolveDrop(box, box.Children[0], DropPosition.Before));  // into its own subtree
        Assert.Null(vm.Workspace.ResolveDrop(a, b, DropPosition.Before));                  // already there
    }

    private static TreeNodeViewModel Node(MainViewModel vm, string name) =>
        vm.Workspace.Tree.SelectMany(n => n.SelfAndDescendants()).First(n => n.Name == name);

    /// <summary>Tree is Box (with one child), a, b.</summary>
    private static TempVault BoxVault()
    {
        var vault = new TempVault();
        vault.Note("a.md");
        vault.Note("b.md");
        vault.Note("Box/x.md");
        return vault;
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
