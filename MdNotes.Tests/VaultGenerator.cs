namespace MdNotes.Tests;

/// <summary>
/// Writes a vault of plausible notes so the app can be driven against a large one. Runs only when
/// <c>MDNOTES_GENERATE_VAULT</c> names a folder, the way <see cref="AppIconGenerator"/> is gated, so a
/// normal test run never writes a vault anywhere.
///
/// The notes are shaped rather than filled: nested folders, sizes that vary by an order of magnitude,
/// a declared title on most of them and a bare file name on the rest, wikilinks between them, and the
/// occasional table, code fence and mermaid diagram. That matters because the things being measured
/// are per-note - the title read, the diacritic fold, the match scan - and a vault of 2000 identical
/// files measures one note 2000 times.
///
/// Deterministic for a given seed, so two runs are comparable and a bug found at note 1174 is still
/// at note 1174 next time.
/// </summary>
public class VaultGenerator
{
    [Fact]
    public void Generate()
    {
        var root = Environment.GetEnvironmentVariable("MDNOTES_GENERATE_VAULT");
        Assert.SkipWhen(
            string.IsNullOrEmpty(root),
            "set MDNOTES_GENERATE_VAULT=<path> to write a vault there; " +
            "MDNOTES_VAULT_NOTES (2000), MDNOTES_VAULT_KB (2), MDNOTES_VAULT_SEED (1) tune it");

        var spec = VaultShape.FromEnvironment();
        var written = VaultShape.Write(root!, spec);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Wrote {written.Notes:N0} notes ({written.Chars:N0} chars) in {written.Folders:N0} folders to {root}");
    }
}
