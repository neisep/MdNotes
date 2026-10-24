using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MdNotes.Models;
using MdNotes.ViewModels;

namespace MdNotes.Views.Shell;

public partial class Sidebar : UserControl
{
    private static readonly DataFormat<string> NodeFormat = DataFormat.CreateInProcessFormat<string>("mdnotes-node");
    private const double DragThreshold = 4;

    private PointerPressedEventArgs? _pressed;
    private TreeNodeViewModel? _pressedNode;
    private Point _pressPoint;
    private Border? _highlightedRow;

    public Sidebar()
    {
        InitializeComponent();

        // Tunnel, not bubble: TreeViewItem marks PointerPressed handled while selecting, so a bubbling
        // handler never sees a left press and no drag could ever be armed.
        VaultTree.AddHandler(InputElement.PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        VaultTree.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        VaultTree.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        VaultTree.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private VaultWorkspaceViewModel Vm => (VaultWorkspaceViewModel)DataContext!;

    /// <summary>The row a left press armed for dragging, once past the movement threshold. Test seam.</summary>
    internal TreeNodeViewModel? PendingDragNode => _pressedNode;

    public void FocusSearchBox() => SearchBox.Focus();

    // Select the item under the pointer before its context menu opens, and arm a possible drag.
    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)
            is not { DataContext: TreeNodeViewModel node }) return;

        var properties = e.GetCurrentPoint(null).Properties;
        if (properties.IsRightButtonPressed)
        {
            Vm.SelectedNode = node;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;
        _pressed = e;
        _pressedNode = node;
        _pressPoint = e.GetPosition(VaultTree);
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e) => ClearPress();

    // The drag starts once the pointer has travelled far enough, so a plain click still selects.
    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } press || _pressedNode is not { } node) return;

        if (!e.GetCurrentPoint(VaultTree).Properties.IsLeftButtonPressed)
        {
            ClearPress();
            return;
        }

        var moved = e.GetPosition(VaultTree) - _pressPoint;
        if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;

        ClearPress();
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(NodeFormat, node.FullPath));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        ClearIndicator();
    }

    private void ClearPress()
    {
        _pressed = null;
        _pressedNode = null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearIndicator();
        e.DragEffects = DragDropEffects.None;

        if (Dragged(e) is not { } node) return;
        var (over, where, row) = HitTest(e);
        if (Vm.ResolveDrop(node, over, where) is null) return;

        e.DragEffects = DragDropEffects.Move;
        ShowIndicator(over, where, row);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ClearIndicator();

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearIndicator();

        if (Dragged(e) is not { } node) return;
        var (over, where, _) = HitTest(e);
        if (Vm.ResolveDrop(node, over, where) is not { } drop) return;

        _ = Vm.MoveNode(node, drop.Folder, drop.Index);
    }

    private TreeNodeViewModel? Dragged(DragEventArgs e) =>
        e.DataTransfer.TryGetValue(NodeFormat) is { } path ? Vm.FindNode(path) : null;

    /// <summary>
    /// The row under the pointer and which edge of it a drop would attach to. The view measures where in
    /// the row the pointer is; what that fraction means is the view model's rule.
    /// </summary>
    private (TreeNodeViewModel? Over, DropPosition Where, Border? Row) HitTest(DragEventArgs e)
    {
        // No row under the pointer: ResolveDrop reads that as the vault root and ignores the position.
        if (RowUnder(e.GetPosition(VaultTree)) is not { DataContext: TreeNodeViewModel node } row)
            return (null, DropPosition.After, null);

        var fraction = e.GetPosition(row).Y / row.Bounds.Height;
        return (node, VaultWorkspaceViewModel.PositionIn(node, fraction), row);
    }

    /// <summary>
    /// The row under the pointer, hit-tested from the point rather than e.Source, which during a drag is
    /// the drop target (the TreeView). PART_LayoutRoot is a TreeViewItem's own row and its bounds exclude
    /// any expanded children, which is exactly what a drop needs - but the name is an internal of the
    /// Fluent theme's template that nothing checks at compile time, so a theme update could rename it and
    /// break drag targeting silently. Internal so SidebarDragTests can fail loudly if that day comes.
    /// </summary>
    internal Border? RowUnder(Point point) =>
        VaultTree.GetVisualsAt(point)
            .SelectMany(v => v.GetSelfAndVisualAncestors())
            .OfType<Border>()
            .FirstOrDefault(b => b.Name == "PART_LayoutRoot");

    // The item template's own Border, which the 'drop' style paints. Inside PART_LayoutRoot, so the
    // hit test finds the row first and this narrows to the part the highlight belongs on.
    private static Border? HighlightIn(Border row) =>
        row.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "Row");

    // Both indicators are the view's own chrome and are put straight on the controls: a highlight
    // written back onto the row's view model would be one more thing to keep alive across a refresh.
    private void ShowIndicator(TreeNodeViewModel? over, DropPosition where, Border? row)
    {
        if (row is null) return;

        if (where is DropPosition.Into && over is not null)
        {
            if (HighlightIn(row) is not { } highlight) return;
            highlight.Classes.Add("drop");
            _highlightedRow = highlight;
            return;
        }

        var edge = where is DropPosition.After ? row.Bounds.Height : 0;
        if (row.TranslatePoint(new Point(0, edge), TreePanel) is not { } point) return;

        DropLine.Margin = new Thickness(8, point.Y - 1, 8, 0);
        DropLine.IsVisible = true;
    }

    private void ClearIndicator()
    {
        DropLine.IsVisible = false;
        if (_highlightedRow is null) return;
        _highlightedRow.Classes.Remove("drop");
        _highlightedRow = null;
    }

    private void OnClearSearch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Vm.SearchQuery = string.Empty;
    }
}
