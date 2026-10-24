using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

public sealed class MatchFinderTests
{
    private static IReadOnlyList<TextMatch> Find(string text, SearchOptions options, string replacement = "X") =>
        new MatchFinder(options, replacement).Find(text);

    [Fact]
    public void Finds_every_occurrence_not_just_the_first()
    {
        var matches = Find("cat dog cat cat", new SearchOptions("cat"));

        Assert.Equal([0, 8, 12], matches.Select(m => m.Offset));
        Assert.All(matches, m => Assert.Equal("cat", m.Found));
    }

    [Fact]
    public void Literal_match_ignores_case_and_accents_by_default()
    {
        var match = Assert.Single(Find("Updated my résumé today", new SearchOptions("RESUME")));

        Assert.Equal("résumé", match.Found); // the span addresses the original, accents intact
    }

    [Fact]
    public void Match_case_is_exact()
    {
        Assert.Empty(Find("Cat", new SearchOptions("cat", MatchCase: true)));
        Assert.Single(Find("Cat", new SearchOptions("Cat", MatchCase: true)));
    }

    [Fact]
    public void Match_case_also_stops_folding_accents()
    {
        Assert.Empty(Find("résumé", new SearchOptions("resume", MatchCase: true)));
    }

    [Fact]
    public void Whole_word_skips_matches_inside_a_word()
    {
        var text = "a note in my notebook and note_id";

        var matches = Find(text, new SearchOptions("note", WholeWord: true));

        Assert.Equal([2], matches.Select(m => m.Offset));
    }

    [Fact]
    public void Regex_expands_group_references()
    {
        var matches = Find("[[Alpha]] and [[Beta]]",
            new SearchOptions(@"\[\[(\w+)\]\]", UseRegex: true), "#$1");

        Assert.Equal(["#Alpha", "#Beta"], matches.Select(m => m.Replacement));
    }

    [Fact]
    public void Regex_honours_whole_word()
    {
        Assert.Empty(Find("notebook", new SearchOptions("note|page", UseRegex: true, WholeWord: true)));
        Assert.Single(Find("a note here", new SearchOptions("note|page", UseRegex: true, WholeWord: true)));
    }

    [Fact]
    public void Regex_skips_zero_length_matches()
    {
        Assert.Empty(Find("bbb", new SearchOptions("a*", UseRegex: true)));
    }

    [Fact]
    public void Invalid_regex_throws_with_a_message()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(
            () => new MatchFinder(new SearchOptions("[", UseRegex: true), ""));

        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void Empty_query_finds_nothing()
    {
        Assert.Empty(Find("anything", new SearchOptions("")));
    }

    [Fact]
    public void Match_carries_its_line_number_and_the_text_either_side()
    {
        var match = Assert.Single(Find("first line\nsecond cat line\nthird", new SearchOptions("cat")));

        Assert.Equal(2, match.LineNumber);
        Assert.Equal("second ", match.Prefix);
        Assert.Equal(" line", match.Suffix);
    }

    [Fact]
    public void Long_lines_are_trimmed_around_the_match()
    {
        var text = new string('a', 200) + "cat" + new string('b', 200);

        var match = Assert.Single(Find(text, new SearchOptions("cat")));

        Assert.StartsWith("…", match.Prefix);
        Assert.EndsWith("…", match.Suffix);
        Assert.True(match.Prefix.Length < 70 && match.Suffix.Length < 70);
    }

    [Fact]
    public void Apply_splices_every_match_and_keeps_the_rest_intact()
    {
        var text = "cat dog cat";
        var matches = Find(text, new SearchOptions("cat"), "fox");

        Assert.Equal("fox dog fox", MatchFinder.Apply(text, matches));
    }

    [Fact]
    public void Apply_preserves_crlf_line_endings()
    {
        var text = "one cat\r\ntwo cat\r\n";
        var matches = Find(text, new SearchOptions("cat"), "dog");

        Assert.Equal("one dog\r\ntwo dog\r\n", MatchFinder.Apply(text, matches));
    }

    /// <summary>
    /// Finding 3: the line index is a pass over the whole text, and most notes in a vault-wide scan have
    /// no match. It is built on the first match and reused for the rest.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_line_index_is_built_only_for_a_text_that_matches(bool regex)
    {
        var finder = new MatchFinder(new SearchOptions("cat", UseRegex: regex), "dog");

        Assert.Empty(finder.Find("no felines here\nat all"));
        Assert.Equal(0, finder.LineIndexesBuilt);

        Assert.Equal(3, finder.Find("cat\ncat\ncat").Count);
        Assert.Equal(1, finder.LineIndexesBuilt);
    }

    /// <summary>A fold the caller already holds is searched instead of folding the text again.</summary>
    [Fact]
    public void A_fold_handed_in_is_what_a_case_blind_search_runs_over()
    {
        var finder = new MatchFinder(new SearchOptions("resume"), "CV");

        var match = Assert.Single(finder.Find("my résumé", DiacriticFolder.Fold("my résumé")));

        Assert.Equal("résumé", match.Found);
    }

    [Fact]
    public void Apply_with_no_matches_returns_the_text_unchanged()
    {
        Assert.Equal("untouched", MatchFinder.Apply("untouched", []));
    }
}
