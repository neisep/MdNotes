using BenchmarkDotNet.Attributes;
using MdNotes.Services;
using MdNotes.Tests;

namespace MdNotes.Benchmarks;

/// <summary>
/// Review finding 3: <c>SearchService.Search</c> folds a full copy of every note on every scan, so the
/// cost of one fold times the note count is the per-keystroke allocation the finding is about. Both
/// paths matter - a note with no accented character returns the original string and allocates nothing,
/// and Swedish prose allocates a copy the size of the note.
/// </summary>
[MemoryDiagnoser]
public class FoldBenchmarks
{
    private string _swedishNote = "";
    private string _asciiNote = "";
    private string _largeSwedishNote = "";

    /// <summary>Longer than the 42 700 chars that put a UTF-16 string on the large object heap.</summary>
    private const int LargeKb = 100;

    [GlobalSetup]
    public void Setup()
    {
        _swedishNote = VaultShape.Note(1, kb: 2, new Random(1));
        _asciiNote = Ascii(_swedishNote);
        _largeSwedishNote = VaultShape.Note(1, LargeKb, new Random(1));
    }

    [Benchmark(Description = "Fold a 2 KB note (Swedish, folds)")]
    public string Note() => DiacriticFolder.Fold(_swedishNote);

    [Benchmark(Description = "Fold a 2 KB note (ASCII, early out)")]
    public string AsciiNote() => DiacriticFolder.Fold(_asciiNote);

    [Benchmark(Description = "Fold a 100 KB note (Swedish, LOH)")]
    public string LargeNote() => DiacriticFolder.Fold(_largeSwedishNote);

    [Benchmark(Description = "Fold a query word")]
    public string Query() => DiacriticFolder.Fold("avstämning");

    private static string Ascii(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++) span[i] = source[i] < 0x80 ? source[i] : 'a';
        });
}
