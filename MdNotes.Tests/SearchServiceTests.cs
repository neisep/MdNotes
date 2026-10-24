using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

public sealed class SearchServiceTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly SearchService _search;

    public SearchServiceTests()
    {
        _vault.Note("Alpha.md", "Hello World from alpha");
        _vault.Note("sub/Beta.md", "nothing relevant");
        _vault.Note("Long.md", new string('a', 200) + " NEEDLE " + new string('b', 200));
        _vault.Note("Jönköping.md", "notes from the trip");
        _vault.Note("Résumé.md", "Updated my résumé in March");
        _search = new SearchService(_vault.Service);
    }

    public void Dispose() => _vault.Dispose();

    /// <summary>
    /// Forwards the run's own cancellation token, so the cases below read as what they are about rather
    /// than repeating it eleven times. Search takes one because the sidebar runs it on the thread pool.
    /// </summary>
    private IReadOnlyList<SearchHit> Search(string query) =>
        _search.Search(query, TestContext.Current.CancellationToken);

    /// <summary>
    /// What F2 was about: searching the vault used to re-read every note on every search. The vault
    /// hands back the same string instance on the second pass, which only the content cache can do -
    /// ReadAllText allocates a new one each time.
    /// </summary>
    [Fact]
    public void Searching_twice_does_not_read_the_vault_twice()
    {
        var path = Path.Combine(_vault.Root, "Alpha.md");

        var first = _vault.Service.Read(path);
        Search("world");

        Assert.Same(first, _vault.Service.Read(path));
    }

    /// <summary>A note edited outside the app must show up in the next search, cache or no cache.</summary>
    [Fact]
    public void A_note_edited_outside_the_app_is_searched_as_it_now_reads()
    {
        Assert.Empty(Search("parsnip"));

        _vault.Note("Alpha.md", "Hello World and a parsnip");

        Assert.Equal(["Alpha"], Search("parsnip").Select(h => h.Name));
    }

    [Fact]
    public void Excluded_and_dot_folders_are_never_searched()
    {
        _vault.Note("node_modules/left/pad.md", "Hello World from a dependency");
        _vault.Note(".git/Stash.md", "Hello World from git");
        _vault.Ignore("node_modules");

        Assert.Equal(["Alpha"], Search("world").Select(h => h.Name));
    }

    [Fact]
    public void Content_match_is_case_insensitive_and_has_snippet()
    {
        var hit = Assert.Single(Search("world"));

        Assert.Equal("Alpha", hit.Name);
        Assert.Contains("World", hit.Snippet);
    }

    [Fact]
    public void Filename_match_has_empty_snippet()
    {
        var hit = Assert.Single(Search("beta"));

        Assert.Equal("Beta", hit.Name);
        Assert.Equal(string.Empty, hit.Snippet);
    }

    [Fact]
    public void Long_content_snippet_is_trimmed_with_ellipses()
    {
        var hit = Assert.Single(Search("needle"));

        Assert.StartsWith("...", hit.Snippet);
        Assert.EndsWith("...", hit.Snippet);
        Assert.Contains("NEEDLE", hit.Snippet);
        Assert.True(hit.Snippet.Length < 100);
    }

    [Fact]
    public void Filename_match_ignores_diacritics()
    {
        var hit = Assert.Single(Search("jonkoping"));

        Assert.Equal("Jönköping", hit.Name);
    }

    [Fact]
    public void Typing_the_diacritics_still_matches()
    {
        var hit = Assert.Single(Search("Jönköping"));

        Assert.Equal("Jönköping", hit.Name);
    }

    [Fact]
    public void Content_match_ignores_diacritics_and_snippet_keeps_them()
    {
        var hit = Assert.Single(Search("resume"));

        Assert.Equal("Résumé", hit.Name);
        Assert.Contains("résumé", hit.Snippet); // the snippet slices the original, not the folded copy
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzz-no-match")]
    public void Blank_or_unmatched_query_returns_nothing(string query) =>
        Assert.Empty(Search(query));

    /// <summary>
    /// Finding 2: naming each result asked the vault per note, and every ask re-checked the exclusion
    /// files on disk. One search is one snapshot, so one check however many notes there are.
    /// </summary>
    [Fact]
    public void One_search_checks_the_exclusion_list_once_not_once_per_note()
    {
        _vault.Ignore("Archive/");
        var settings = (FakeSettings)_vault.Settings;
        settings.GitIgnoreReads = 0;

        Assert.NotEmpty(Search("o"));

        Assert.Equal(1, settings.GitIgnoreReads);
    }
}
