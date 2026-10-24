using BenchmarkDotNet.Attributes;
using MdNotes.Services;

namespace MdNotes.Benchmarks;

/// <summary>
/// Review finding 5: <c>QuickSwitcherViewModel.RefreshResults</c> calls <see cref="NameMatch.Rank"/>
/// twice per note - once for the title, once for the file name - and each call folds both sides, so a
/// 5 000-note vault folds the query 10 000 times per keystroke. These rows are the per-call cost the
/// switcher row in <see cref="SwitcherBenchmarks"/> multiplies out.
///
/// The three cases are the three shapes of the work: a miss walks every tier, a prefix hit stops at
/// the third, and an exact hit returns before anything folds at all.
/// </summary>
[MemoryDiagnoser]
public class NameMatchBenchmarks
{
    private const string Candidate = "årsredovisning 01174";

    [Benchmark(Description = "Rank, no match (walks every tier)")]
    public NameRank Miss() => NameMatch.Rank(Candidate, "kalkylark");

    [Benchmark(Description = "Rank, prefix match")]
    public NameRank Prefix() => NameMatch.Rank(Candidate, "årsred");

    [Benchmark(Description = "Rank, prefix match on the folded form")]
    public NameRank PrefixFolded() => NameMatch.Rank(Candidate, "arsred");

    [Benchmark(Description = "Rank, exact match (no fold)")]
    public NameRank Exact() => NameMatch.Rank(Candidate, Candidate);
}
