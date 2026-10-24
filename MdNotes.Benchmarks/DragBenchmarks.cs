using System.Collections.ObjectModel;
using BenchmarkDotNet.Attributes;
using MdNotes.ViewModels;

namespace MdNotes.Benchmarks;

/// <summary>
/// Review finding 6: every <c>DragOver</c> resolves the dragged path with
/// <c>VaultWorkspaceViewModel.FindNode</c> and then resolves it again through
/// <c>ResolveDrop</c> → <c>SiblingsOf</c>, so one pointer move walks the tree twice. The review says
/// to confirm the lag on a 5 000-note vault before adding parent links and a path index.
///
/// The lookup is measured on a real reconciled tree rather than through the view model, which would
/// need the whole service set built to time two lines. Those two lines are reproduced here exactly -
/// <c>Tree.SelectMany(n =&gt; n.SelfAndDescendants()).FirstOrDefault(...)</c>, ordinal-ignore-case - so
/// what moves when task 62 lands is what is measured here.
///
/// First and last are the two ends of the walk: a node in the first folder answers immediately, one
/// at the end costs the whole tree. A pointer move over the bottom of the sidebar pays twice the last.
///
/// Task 62 replaced the walk with a path index rebuilt per reconcile; the walk is kept here as the
/// "before" row, and the index's lookup and its rebuild are measured beside it.
/// </summary>
[MemoryDiagnoser]
public class DragBenchmarks
{
    private BenchVault _vault = null!;
    private ObservableCollection<TreeNodeViewModel> _tree = null!;
    private string _first = "";
    private string _last = "";
    private string _missing = "";
    private readonly Dictionary<string, TreeNodeViewModel> _byPath = new(StringComparer.OrdinalIgnoreCase);

    [Params(500, 5_000)]
    public int Notes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _vault = new BenchVault(Notes);
        _tree = [];
        TreeNodeViewModel.Reconcile(_tree, _vault.Service.GetTree());

        var all = _tree.SelectMany(n => n.SelfAndDescendants()).ToList();
        _first = all[0].FullPath;
        _last = all[^1].FullPath;
        _missing = Path.Combine(_vault.Root, "not-a-note.md");
        Console.WriteLine($"Tree nodes: {all.Count:N0}");
        Index();
    }

    [GlobalCleanup]
    public void Cleanup() => _vault.Dispose();

    [Benchmark(Description = "FindNode, first node in the tree")]
    public TreeNodeViewModel? First() => FindNode(_first);

    [Benchmark(Description = "FindNode, last node in the tree")]
    public TreeNodeViewModel? Last() => FindNode(_last);

    [Benchmark(Description = "FindNode, no such node (full walk)")]
    public TreeNodeViewModel? Missing() => FindNode(_missing);

    [Benchmark(Description = "Index lookup, last node in the tree")]
    public TreeNodeViewModel? IndexedLast() => _byPath.GetValueOrDefault(_last);

    [Benchmark(Description = "Index rebuild, once per reconcile")]
    public int Index()
    {
        _byPath.Clear();
        foreach (var node in TreeNodeViewModel.Walk(_tree)) _byPath.TryAdd(node.FullPath, node);
        return _byPath.Count;
    }

    /// <summary>
    /// The pre-task-62 lookup's shape. <c>SelfAndDescendants</c> under it is now the explicit-stack walk,
    /// so these rows measure the old search over the new iterator, not the original nested one.
    /// </summary>
    private TreeNodeViewModel? FindNode(string path) =>
        _tree.SelectMany(n => n.SelfAndDescendants())
            .FirstOrDefault(n => string.Equals(n.FullPath, path, StringComparison.OrdinalIgnoreCase));
}
