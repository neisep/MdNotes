using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using MdNotes.Services;
using MdNotes.ViewModels;
using MdNotes.Views;
using MdNotes.Views.Markdown;
using MdNotes.Views.Shell;

namespace MdNotes.Tests;

/// <summary>
/// The wiring a drag needs before any of it can happen. Headless cannot run a real drag
/// (DoDragDropAsync blocks on a platform drag loop), so this covers the arming step and the row
/// chrome the drag drives — the arming step is exactly where TreeViewItem's handled PointerPressed
/// broke the first version.
/// </summary>
public class SidebarDragTests
{
    [AvaloniaFact]
    public async Task Tree_accepts_drops()
    {
        var (_, _, tree, _, _) = await ShellAsync();
        Assert.True(DragDrop.GetAllowDrop(tree));
    }

    [AvaloniaFact]
    public async Task Left_press_on_a_row_arms_a_drag_even_though_the_row_handles_the_event()
    {
        var (window, sidebar, _, row, _) = await ShellAsync();

        window.MouseDown(Centre(row, window), MouseButton.Left);

        Assert.Equal("a", sidebar.PendingDragNode?.Name);
    }

    [AvaloniaFact]
    public async Task Releasing_disarms_the_drag()
    {
        var (window, sidebar, _, row, _) = await ShellAsync();
        var centre = Centre(row, window);

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Right);

        Assert.Null(sidebar.PendingDragNode);
    }

    [AvaloniaFact]
    public async Task Right_press_selects_without_arming_a_drag()
    {
        var (window, sidebar, _, row, vm) = await ShellAsync();

        window.MouseDown(Centre(row, window), MouseButton.Right);

        Assert.Equal("a", vm.Workspace.SelectedNode?.Name);
        Assert.Null(sidebar.PendingDragNode);
    }

    /// <summary>
    /// The drop highlight is the view's own now, put on the row control rather than written back onto
    /// the row's view model. Headless cannot run the drag that sets it, so this checks the two things
    /// the drag depends on: the named Border is where the code-behind looks for it, and the 'drop'
    /// class it adds really does paint it.
    /// </summary>
    [AvaloniaFact]
    public async Task The_row_border_takes_the_drop_class()
    {
        var (window, _, _, row, _) = await ShellAsync();

        var border = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Row");
        var plain = border.Background;

        border.Classes.Add("drop");
        window.CaptureRenderedFrame();
        var highlighted = border.Background;

        border.Classes.Remove("drop");
        window.CaptureRenderedFrame();

        Assert.NotNull(highlighted);
        Assert.NotEqual(plain, highlighted);
        Assert.Equal(plain, border.Background);
    }

    /// <summary>
    /// Drag targeting hangs on PART_LayoutRoot, an internal name in Fluent's TreeViewItem template that no
    /// compiler checks. A theme update that renames it would leave dropping quietly broken - every drop
    /// landing at the vault root, with nothing to see in a build log. This fails instead.
    /// </summary>
    [AvaloniaFact]
    public async Task The_row_under_the_pointer_resolves_through_the_fluent_template()
    {
        var (_, sidebar, tree, row, _) = await ShellAsync();

        var found = sidebar.RowUnder(Centre(row, tree));

        Assert.NotNull(found);
        Assert.Same(row.DataContext, found.DataContext);
        Assert.True(found.Bounds.Height > 0);
    }

    private static Point Centre(Visual row, Visual window) =>
        row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;

    private static async Task<(Window Window, Sidebar Sidebar, TreeView Tree, TreeViewItem Row, MainViewModel Vm)> ShellAsync()
    {
        var vault = new TempVault();
        vault.Note("a.md");
        vault.Note("b.md");

        using var vm = new MainViewModel(TestServices.For(vault));

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Assert.True(await vm.TryOpenInitialVaultAsync());
        window.CaptureRenderedFrame();

        var sidebar = window.GetVisualDescendants().OfType<Sidebar>().Single();
        var tree = sidebar.GetVisualDescendants().OfType<TreeView>().Single();
        var row = tree.GetVisualDescendants().OfType<TreeViewItem>().First();
        return (window, sidebar, tree, row, vm);
    }
}
