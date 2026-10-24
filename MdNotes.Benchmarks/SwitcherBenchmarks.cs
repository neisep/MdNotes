using BenchmarkDotNet.Attributes;
using MdNotes.ViewModels;

namespace MdNotes.Benchmarks;

/// <summary>
/// Review finding 5: every keystroke in the quick switcher re-flattens the whole tree into fresh
/// <c>SwitcherItem</c>s, ranks each note twice, and sorts every candidate to take fifty. The review
/// asks for this over five thousand notes before any cache is added, because the answer decides
/// whether task 61 is worth doing at all.
///
/// The three queries are the three costs. An empty query skips ranking and sorts by name; a narrow
/// one ranks every note and sorts a handful; a broad one ranks every note and sorts most of them.
/// Typing a word walks all three in order.
/// </summary>
[MemoryDiagnoser]
public class SwitcherBenchmarks
{
    private BenchVault _vault = null!;
    private QuickSwitcherViewModel _empty = null!;
    private QuickSwitcherViewModel _narrow = null!;
    private QuickSwitcherViewModel _broad = null!;

    [Params(500, 5_000)]
    public int Notes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _vault = new BenchVault(Notes);
        // The tree walk is not what is being measured; warm it so the first iteration is like the rest.
        _vault.Service.GetTree();

        _empty = Switcher("");
        _narrow = Switcher("arsredovisning 01174");
        _broad = Switcher("note-0");
    }

    [GlobalCleanup]
    public void Cleanup() => _vault.Dispose();

    [Benchmark(Description = "Keystroke, empty query (lists everything)")]
    public int Empty() => Refresh(_empty);

    [Benchmark(Description = "Keystroke, narrow query (few hits)")]
    public int Narrow() => Refresh(_narrow);

    [Benchmark(Description = "Keystroke, broad query (most notes hit)")]
    public int Broad() => Refresh(_broad);

    /// <summary>
    /// The query is set once, in setup, so an iteration is exactly the work one keystroke causes -
    /// assigning Query here would refresh through the setter as well and time two.
    /// </summary>
    private QuickSwitcherViewModel Switcher(string query) =>
        new(_vault.Service, _ => { }, _ => { }) { Query = query };

    private static int Refresh(QuickSwitcherViewModel switcher)
    {
        switcher.RefreshResults();
        return switcher.Results.Count;
    }
}
