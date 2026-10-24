using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

public sealed class ReplaceServiceTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly ReplaceService _replace;

    public ReplaceServiceTests()
    {
        _vault.Note("Alpha.md", "the widget is a widget");
        _vault.Note("sub/Beta.md", "one widget here");
        _vault.Note("sub/Gamma.md", "nothing to see");
        _vault.Note(".hidden/Ignored.md", "a widget nobody should rewrite");
        _replace = new ReplaceService(_vault.Service);
    }

    public void Dispose() => _vault.Dispose();

    private IReadOnlyList<NoteMatches> Find(string query, string replacement = "gadget", string? scope = null) =>
        _replace.Find(new SearchOptions(query, FolderScope: scope), replacement);

    [Fact]
    public void Finds_every_match_in_every_note_in_the_vault()
    {
        var results = Find("widget");

        Assert.Equal(["Alpha", "Beta"], results.Select(r => r.Name).Order());
        Assert.Equal(2, results.Single(r => r.Name == "Alpha").Matches.Count);
    }

    [Fact]
    public void Notes_carry_their_folder_relative_to_the_vault_root()
    {
        var beta = Find("widget").Single(r => r.Name == "Beta");

        Assert.Equal("sub", beta.Folder);
    }

    [Fact]
    public void Folder_scope_excludes_everything_outside_it()
    {
        var results = Find("widget", scope: Path.Combine(_vault.Root, "sub"));

        Assert.Equal("Beta", Assert.Single(results).Name);
    }

    [Fact]
    public void Dot_folders_are_never_scanned()
    {
        Assert.DoesNotContain(Find("widget"), r => r.Name == "Ignored");
    }

    [Fact]
    public void Excluded_folders_are_never_scanned()
    {
        _vault.Note("node_modules/Readme.md", "one widget here");
        _vault.Ignore("node_modules");

        Assert.DoesNotContain(Find("widget"), r => r.Name == "Readme");
    }

    [Fact]
    public void Empty_query_finds_nothing()
    {
        Assert.Empty(Find(""));
    }

    [Fact]
    public void Replace_writes_only_the_notes_it_was_given()
    {
        var alpha = Find("widget").Single(r => r.Name == "Alpha");

        var report = _replace.Replace([alpha]);

        Assert.Equal(1, report.NotesChanged);
        Assert.Equal(2, report.Replacements);
        Assert.Equal("the gadget is a gadget", File.ReadAllText(Path.Combine(_vault.Root, "Alpha.md")));
        Assert.Equal("one widget here", File.ReadAllText(Path.Combine(_vault.Root, "sub", "Beta.md")));
    }

    [Fact]
    public void Replace_applies_only_the_matches_it_was_given()
    {
        var alpha = Find("widget").Single(r => r.Name == "Alpha");
        var first = alpha with { Matches = [alpha.Matches[0]] };

        _replace.Replace([first]);

        Assert.Equal("the gadget is a widget", File.ReadAllText(Path.Combine(_vault.Root, "Alpha.md")));
    }

    [Fact]
    public void A_note_changed_on_disk_since_the_search_is_skipped_whole()
    {
        var results = Find("widget");
        File.WriteAllText(Path.Combine(_vault.Root, "Alpha.md"), "someone else edited this");

        var report = _replace.Replace(results);

        Assert.Equal(Path.Combine(_vault.Root, "Alpha.md"), Assert.Single(report.Skipped));
        Assert.Equal("someone else edited this", File.ReadAllText(Path.Combine(_vault.Root, "Alpha.md")));
        Assert.Equal(1, report.NotesChanged); // Beta still went through
    }

    [Fact]
    public void Restoring_the_snapshot_undoes_the_whole_replace()
    {
        var before = _vault.Service.GetNotes().ToDictionary(p => p, File.ReadAllText);

        var report = _replace.Replace(Find("widget"));
        Assert.Equal(2, report.NotesChanged);

        _replace.Restore(report.Snapshot);

        foreach (var (path, text) in before)
            Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void Regex_replace_migrates_a_syntax_across_the_vault()
    {
        _vault.Note("Links.md", "see [[Alpha]] and [[Beta]]");

        var results = _replace.Find(new SearchOptions(@"\[\[(\w+)\]\]", UseRegex: true), "#$1", TestContext.Current.CancellationToken);
        _replace.Replace(results);

        Assert.Equal("see #Alpha and #Beta", File.ReadAllText(Path.Combine(_vault.Root, "Links.md")));
    }
}
