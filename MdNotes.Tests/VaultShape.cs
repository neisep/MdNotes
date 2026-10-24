namespace MdNotes.Tests;

/// <summary>
/// How a generated vault is built, and the one definition of what a generated note looks like - shared
/// so ScanBenchmark and the MdNotes.Benchmarks micro-benchmarks measure the same shape the app is
/// driven against. Linked into MdNotes.Benchmarks as source, so there is still only one copy.
/// </summary>
internal static class VaultShape
{
    /// <summary>The share of notes carrying <see cref="Needle"/>, so a search has a realistic hit list.</summary>
    public const int HitEveryNth = 20;

    /// <summary>A word no lorem filler produces, planted so a benchmark query means something.</summary>
    public const string Needle = "kalkylark";

    public sealed record Spec(int Notes, int AverageKb, int Seed);

    public sealed record Written(int Notes, int Folders, long Chars);

    public static Spec FromEnvironment() => new(
        Int("MDNOTES_VAULT_NOTES", 2000),
        Int("MDNOTES_VAULT_KB", 2),
        Int("MDNOTES_VAULT_SEED", 1));

    /// <summary>
    /// Writes the vault. Refuses a folder that already holds notes: this is pointed at by hand, and
    /// overwriting someone's real vault is not a mistake worth leaving available.
    /// </summary>
    public static Written Write(string root, Spec spec)
    {
        if (Directory.Exists(root) && Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Any())
            throw new IOException($"'{root}' already holds notes. Generate into an empty or new folder.");

        Directory.CreateDirectory(root);

        var random = new Random(spec.Seed);
        var folders = Folders(root, spec.Notes);
        foreach (var folder in folders) Directory.CreateDirectory(folder);

        long chars = 0;
        for (var i = 0; i < spec.Notes; i++)
        {
            var text = Note(i, spec, random);
            chars += text.Length;
            File.WriteAllText(Path.Combine(folders[random.Next(folders.Count)], FileName(i, random)), text);
        }

        return new Written(spec.Notes, folders.Count, chars);
    }

    /// <summary>
    /// A nested hierarchy rather than a flat list, because the walk is recursive and a folder costs a
    /// directory enumeration of its own. Roughly one folder per twenty notes, three levels deep.
    /// </summary>
    private static List<string> Folders(string root, int notes)
    {
        var folders = new List<string> { root };
        var wanted = Math.Max(1, notes / 20);

        for (var i = 0; folders.Count < wanted; i++)
        {
            var area = Path.Combine(root, Areas[i % Areas.Length] + (i < Areas.Length ? "" : $"-{i / Areas.Length}"));
            folders.Add(area);
            folders.Add(Path.Combine(area, "notes"));
            folders.Add(Path.Combine(area, "archive"));
        }

        return folders;
    }

    /// <summary>
    /// Most notes get a declared title one line in, a few only a file name - the two rows the sidebar
    /// has to render differently, and the branch NoteTitle.From has to take both ways.
    /// </summary>
    private static string Note(int index, Spec spec, Random random)
    {
        // Sizes spread over an order of magnitude around the average: most notes are short and a few
        // are long, which is what makes the content cache's character budget behave like it does in life.
        var kb = Math.Max(1, (int)Math.Round(spec.AverageKb * Math.Pow(10, random.NextDouble() - 0.5)));
        return Note(index, kb, random);
    }

    /// <summary>
    /// One note of the generated shape, filled to <paramref name="kb"/> KB. The filler a note is made
    /// of follows from <paramref name="index"/>: 0 tables, 3 code fences, 7 mermaid diagrams, the rest
    /// prose. Public so a micro-benchmark measures the same text a generated vault holds.
    /// </summary>
    public static string Note(int index, int kb, Random random)
    {
        var body = new System.Text.StringBuilder();

        if (index % 7 != 0) body.Append($"# {Title(index)}\n\n");

        body.Append($"Skriven {2024 + index % 2}-{1 + index % 12:D2}-{1 + index % 28:D2}. ");
        body.Append($"Se även [[{Title(Math.Max(0, index - 1))}]] och [[{Title(index + 1)}]].\n\n");

        if (index % HitEveryNth == 0) body.Append($"Detta är ett {Needle} med siffror som inte stämmer.\n\n");

        while (body.Length < kb * 1024)
        {
            body.Append((index % 11) switch
            {
                0 => Table(random),
                3 => Fence(random),
                7 => Diagram(),
                _ => Paragraph(random),
            });
        }

        return body.ToString();
    }

    private static string Paragraph(Random random) =>
        string.Join(' ', Enumerable.Range(0, 30 + random.Next(60)).Select(_ => Words[random.Next(Words.Length)]))
        + ".\n\n";

    private static string Table(Random random) =>
        "| Konto | Belopp | Status |\n|---|---|---|\n"
        + string.Concat(Enumerable.Range(0, 3 + random.Next(6))
            .Select(r => $"| {1000 + random.Next(9000)} | {random.Next(100000) / 100.0:N2} | {Words[random.Next(Words.Length)]} |\n"))
        + "\n";

    private static string Fence(Random random) =>
        "```csharp\npublic sealed record Rad(string Konto, decimal Belopp);\n"
        + string.Concat(Enumerable.Range(0, 2 + random.Next(8)).Select(r => $"// rad {r}: {Words[random.Next(Words.Length)]}\n"))
        + "```\n\n";

    private static string Diagram() =>
        "```mermaid\nflowchart TD\n  A[Underlag] --> B{Attest}\n  B -->|Ja| C[Bokförs]\n  B -->|Nej| D[Retur]\n```\n\n";

    private static string Title(int index) => $"{Areas[index % Areas.Length]} {index:D5}";

    /// <summary>
    /// A name that is not the title, and every so often one with diacritics and spaces - the folding
    /// path and the path-escaping path both want a row in a vault meant for driving the app.
    /// </summary>
    private static string FileName(int index, Random random) => random.Next(5) == 0
        ? $"Översikt {index:D5}.md"
        : $"note-{index:D5}.md";

    private static readonly string[] Areas =
        ["bokforing", "lonehantering", "arsredovisning", "projekt", "moten", "kunder", "rutiner", "utbildning"];

    private static readonly string[] Words =
    [
        "faktura", "underlag", "attest", "period", "avstämning", "bokföring", "resultat", "balans",
        "leverantör", "kund", "moms", "konto", "verifikat", "rapport", "budget", "utfall",
        "lön", "semester", "avtal", "rutin", "möte", "beslut", "uppföljning", "avvikelse",
    ];

    private static int Int(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var parsed) && parsed > 0
            ? parsed
            : fallback;
}
