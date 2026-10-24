using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MdNotes.Models;

namespace MdNotes.ViewModels;

/// <summary>
/// One row of the sidebar tree. <see cref="FullPath"/> is its identity: it is what the vault, the
/// wikilinks and <see cref="Reconcile"/> all match on, and the only thing that stays fixed for the
/// life of the object. Everything else on it is what the sidebar has hung there - today expansion,
/// tomorrow whatever else - and lives exactly as long as the row does.
/// </summary>
public sealed partial class TreeNodeViewModel(string name, string fullPath, bool isFolder) : ObservableObject
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public bool IsFolder { get; } = isFolder;
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    /// <summary>
    /// The folder row this one sits in, or null at the vault root. Fixed for the life of the row: a
    /// node is matched by its path, so one that moves to another folder is a new row, never this one
    /// re-parented. It is what lets siblings and ancestors be reached without searching the tree.
    /// </summary>
    public TreeNodeViewModel? Parent { get; private init; }

    [ObservableProperty] public partial bool IsExpanded { get; set; }

    /// <summary>
    /// What the row shows: the note's declared title when there is one, else <see cref="Name"/>.
    /// Observable and re-assigned by <see cref="Reconcile"/> rather than fixed at construction, because
    /// a row survives a refresh by design - editing a heading has to reach the row that is already there.
    /// </summary>
    [ObservableProperty] public partial string Display { get; private set; } = name;

    /// <summary>
    /// Matches the entries read off disk onto the nodes already there, by path. A row that is still
    /// in the vault stays the same object, and so keeps everything the sidebar hung on it - its
    /// expansion, the TreeView's own selection and container, the scroll position, and whatever is
    /// hung on it next. Nothing has to be remembered across a refresh because nothing is thrown away.
    /// </summary>
    /// <param name="parent">The row <paramref name="nodes"/> belongs to; null for the vault root.</param>
    public static void Reconcile(
        ObservableCollection<TreeNodeViewModel> nodes, IReadOnlyList<VaultEntry> entries,
        TreeNodeViewModel? parent = null)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
            if (IndexOf(entries, nodes[i]) < 0) nodes.RemoveAt(i);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var at = IndexOf(nodes, entry);
            if (at < 0)
            {
                nodes.Insert(i, FromEntry(entry, parent));
                continue;
            }

            // Move, not remove-and-insert: the TreeView keeps the container, and with it the row.
            if (at != i) nodes.Move(at, i);
            nodes[i].Display = entry.Display;
            Reconcile(nodes[i].Children, entry.Children, nodes[i]);
        }
    }

    private static TreeNodeViewModel FromEntry(VaultEntry entry, TreeNodeViewModel? parent)
    {
        var node = new TreeNodeViewModel(entry.Name, entry.FullPath, entry.IsFolder)
        {
            Display = entry.Display,
            Parent = parent,
        };
        foreach (var child in entry.Children)
            node.Children.Add(FromEntry(child, node));
        return node;
    }

    /// <summary>This row and everything under it, top to bottom as the sidebar draws them.</summary>
    public IEnumerable<TreeNodeViewModel> SelfAndDescendants() => Walk([this]);

    /// <summary>
    /// Every row in <paramref name="roots"/> and under them, in drawing order. An explicit stack rather
    /// than nested iterators: a recursive <c>yield</c> resumes one iterator per level for every row it
    /// hands out, which made a whole-tree walk cost O(depth) per node.
    /// </summary>
    public static IEnumerable<TreeNodeViewModel> Walk(IReadOnlyList<TreeNodeViewModel> roots)
    {
        var pending = new Stack<TreeNodeViewModel>();
        for (var i = roots.Count - 1; i >= 0; i--) pending.Push(roots[i]);

        while (pending.TryPop(out var node))
        {
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) pending.Push(node.Children[i]);
        }
    }

    // Linear rather than a dictionary: this runs over one folder's children, not the whole tree, the
    // unchanged case answers on the first comparison, and a path-keyed dictionary would have to decide
    // what 'a.md' and 'A.md' mean - two different notes on Linux.
    private static int IndexOf(IReadOnlyList<VaultEntry> entries, TreeNodeViewModel node)
    {
        for (var i = 0; i < entries.Count; i++)
            if (Same(entries[i], node)) return i;
        return -1;
    }

    private static int IndexOf(IReadOnlyList<TreeNodeViewModel> nodes, VaultEntry entry)
    {
        for (var i = 0; i < nodes.Count; i++)
            if (Same(entry, nodes[i])) return i;
        return -1;
    }

    /// <summary>
    /// Ordinal, deliberately stricter than VaultWorkspaceViewModel.FindNode. That one answers "which
    /// row does this path a caller handed me mean", where ignoring case is right. This one compares two
    /// readings of the same folder, where the only way the strings can differ is that the thing was
    /// renamed - and a row keeping a path the disk no longer has would send the editor, the rename and
    /// the delete to a file that is not there. The folder flag is half the identity too: a path that
    /// stopped being a folder is a different row, not the same row lying about itself.
    /// </summary>
    private static bool Same(VaultEntry entry, TreeNodeViewModel node) =>
        node.IsFolder == entry.IsFolder
        && string.Equals(node.FullPath, entry.FullPath, StringComparison.Ordinal);
}
