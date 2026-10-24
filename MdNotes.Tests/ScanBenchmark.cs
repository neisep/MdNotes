using System.Diagnostics;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// Not an assertion: a stopwatch over the three vault-wide scans that run on the UI thread - the tree
/// walk, the sidebar search and the replace overlay's find. Performance review finding 1 says to
/// measure the freeze before moving them off it, because on a small local vault the current design
/// may well be adequate.
///
/// Times the real services as they stand, so nothing has to be instrumented in MdNotes and nothing
/// has to be taken back out afterwards. Skipped unless MDNOTES_BENCH=1, so 'dotnet test' is unaffected.
///
/// MDNOTES_BENCH_VAULT=&lt;path&gt; points it at a real vault, which is the run that decides this: a
/// generated vault on a local disk measures the CPU cost and none of the latency, and latency is the
/// case the finding is about. Every run after the first also has the OS file cache warm, so even a
/// real vault understates the first open after a reboot - read the run-1 column, not just the median.
/// </summary>
public sealed class ScanBenchmark(ITestOutputHelper output)
{
    private const int Runs = 5;

    [Fact]
    public void Measure()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("MDNOTES_BENCH") != "1",
            "set MDNOTES_BENCH=1 to measure; MDNOTES_BENCH_VAULT=<path> measures a real vault instead of a " +
            "generated one, and MDNOTES_VAULT_NOTES / MDNOTES_VAULT_KB / MDNOTES_VAULT_SEED shape the generated one");

        var given = Environment.GetEnvironmentVariable("MDNOTES_BENCH_VAULT");
        var generated = given is null ? Generate() : null;
        var root = given ?? generated!;

        try
        {
            Report(root, generated is not null);
        }
        finally
        {
            if (generated is not null) Directory.Delete(generated, recursive: true);
        }
    }

    private void Report(string root, bool isGenerated)
    {
        var settings = new FakeSettings();

        output.WriteLine($"Vault: {root}{(isGenerated ? "  (generated)" : "  (real)")}");
        output.WriteLine($"Query: \"{VaultShape.Needle}\"   runs: {Runs}");
        output.WriteLine("");

        // Sized before anything is timed, so the budget line is available to read the warm rows with.
        using (var sizing = Opened(settings))
        {
            var notes = sizing.GetNotes();
            var cache = new NoteContentCache();
            foreach (var note in notes) cache.Read(note);

            output.WriteLine($"Notes: {notes.Count}");
            output.WriteLine(
                $"Content: {cache.HeldChars:N0} chars held for {cache.Count:N0} of them, " +
                $"against the cache's {cache.BudgetChars:N0} budget.");
            if (cache.Count < notes.Count)
                output.WriteLine(
                    "  OVER BUDGET: the cache cannot hold this vault, so every scan re-reads it - " +
                    "the warm rows below are really cold ones, and review finding 4 (eviction) applies.");
            output.WriteLine("");
        }

        output.WriteLine($"{"",-38}{"run 1",9}{"min",9}{"median",9}{"max",9}");

        settings.UseNoteTitles = true;
        Row("1. Tree walk, cold, titles on", Cold(v => v.GetTree()));

        settings.UseNoteTitles = false;
        Row("2. Tree walk, cold, titles off", Cold(v => v.GetTree()));

        settings.UseNoteTitles = true;
        Row("3. Tree walk, re-walk, warm", Warm(v => v.GetTree(), v => v.Invalidate()));

        Row("4. Search, cold", Cold(v => new SearchService(v).Search(VaultShape.Needle)));
        Row("5. Search, warm", WarmSearch());
        Row("6. Replace find, warm", WarmFind());

        output.WriteLine("");
        output.WriteLine("Under ~50 ms is invisible; ~50-200 ms is felt; over ~200 ms is a freeze.");

        Allocations();

        VaultService Opened(ISettingsService s)
        {
            var vault = new VaultService(s);
            vault.Open(root);
            return vault;
        }

        // A fresh service per run: no snapshot, no content cache. What opening a vault costs.
        double[] Cold(Action<VaultService> body) => Times(() =>
        {
            using var vault = Opened(settings);
            return Time(() => body(vault));
        });

        // One service, warmed once, then made to do the work again. What a watcher storm costs.
        double[] Warm(Action<VaultService> body, Action<VaultService> reset)
        {
            using var vault = Opened(settings);
            body(vault);
            return Times(() =>
            {
                reset(vault);
                return Time(() => body(vault));
            });
        }

        double[] WarmSearch()
        {
            using var vault = Opened(settings);
            var search = new SearchService(vault);
            search.Search(VaultShape.Needle);
            return Times(() => Time(() => search.Search(VaultShape.Needle)));
        }

        double[] WarmFind()
        {
            using var vault = Opened(settings);
            var replace = new ReplaceService(vault);
            var options = new SearchOptions(VaultShape.Needle);
            replace.Find(options, "spreadsheet");
            return Times(() => Time(() => replace.Find(options, "spreadsheet")));
        }

        void Row(string label, double[] ms) => output.WriteLine(
            $"{label,-38}{ms[0],9:N1}{ms.Min(),9:N1}{Median(ms),9:N1}{ms.Max(),9:N1}");

        // What 'dotnet-counters monitor -p <pid> System.Runtime' would show while a word is typed into
        // the sidebar search box, measured instead of watched: the same debounced scans run here, and
        // the runtime's own counters are read around them. Findings 3 and 4 are both about this number -
        // 3 that every scan folds a copy of every note, 4 that above the cache budget every scan also
        // re-reads the vault. Typing one word is one scan per keystroke, which is the multiplier.
        void Allocations()
        {
            using var vault = Opened(settings);
            var search = new SearchService(vault);

            // Warm: the first scan reads the vault off disk, and that is measured in the rows above.
            search.Search(VaultShape.Needle);

            var before = Snapshot();
            foreach (var keystroke in Keystrokes(VaultShape.Needle))
                search.Search(keystroke);
            var after = Snapshot();

            var strokes = VaultShape.Needle.Length;
            output.WriteLine("");
            output.WriteLine($"Typing \"{VaultShape.Needle}\" one character at a time - {strokes} debounced scans, cache warm:");
            output.WriteLine($"  Allocated      {(after.Bytes - before.Bytes) / 1024.0 / 1024.0,10:N1} MB" +
                             $"   ({(after.Bytes - before.Bytes) / 1024.0 / 1024.0 / strokes:N1} MB per keystroke)");
            output.WriteLine($"  Gen0 / 1 / 2   {after.Gen0 - before.Gen0,10:N0} / {after.Gen1 - before.Gen1:N0} / {after.Gen2 - before.Gen2:N0}");
            output.WriteLine($"  LOH size       {after.Loh / 1024.0 / 1024.0,10:N1} MB after the last scan");
            output.WriteLine("A note over ~42,700 chars folds into a string on the large object heap, which is what");
            output.WriteLine("makes the LOH row move; the allocation is a whole second copy of the vault per scan.");
        }
    }

    private readonly record struct Counters(long Bytes, int Gen0, int Gen1, int Gen2, long Loh);

    /// <summary>
    /// Collected first, so what is compared is what survived rather than what happened to be pending.
    /// Process-wide, which is what a counters session sees too.
    /// </summary>
    private static Counters Snapshot()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        var loh = GC.GetGCMemoryInfo().GenerationInfo is { Length: > 3 } generations
            ? generations[3].SizeAfterBytes
            : 0;
        return new Counters(
            GC.GetTotalAllocatedBytes(precise: true),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), loh);
    }

    /// <summary>"kalk" typed out: the prefixes a debounced search box actually runs a scan for.</summary>
    private static IEnumerable<string> Keystrokes(string word) =>
        Enumerable.Range(1, word.Length).Select(length => word[..length]);

    private static double[] Times(Func<double> run) => [.. Enumerable.Range(0, Runs).Select(_ => run())];

    private static double Time(Action body)
    {
        var clock = Stopwatch.StartNew();
        body();
        return clock.Elapsed.TotalMilliseconds;
    }

    private static double Median(double[] values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    /// <summary>
    /// A throwaway vault of the same shape <see cref="VaultGenerator"/> writes, so the numbers here and
    /// the app being driven by hand are measuring the same thing.
    /// </summary>
    private string Generate()
    {
        var root = Directory.CreateTempSubdirectory("mdnotes-bench-").FullName;
        var spec = VaultShape.FromEnvironment();
        var written = VaultShape.Write(root, spec);

        output.WriteLine($"Generated {written.Notes:N0} notes ({written.Chars:N0} chars) in {written.Folders:N0} folders.");
        return root;
    }
}
