using MdNotes.Services;

namespace MdNotes.Tests;

public sealed class NameMatchTests
{
    [Theory]
    [InlineData("Résumé", "Résumé", NameRank.Exact)]
    [InlineData("Résumé", "résumé", NameRank.Exact)]
    [InlineData("Résumé", "Resume", NameRank.ExactFolded)]
    [InlineData("Jönköping", "jonkoping", NameRank.ExactFolded)]
    [InlineData("Résumé 2026", "Résumé", NameRank.Prefix)]
    [InlineData("Résumé 2026", "resume", NameRank.PrefixFolded)]
    [InlineData("Old Résumé", "Résumé", NameRank.Contains)]
    [InlineData("Old Résumé", "resume", NameRank.ContainsFolded)]
    [InlineData("Résumé", "budget", NameRank.None)]
    public void Rank_orders_exact_before_folded(string candidate, string query, NameRank expected) =>
        Assert.Equal(expected, NameMatch.Rank(candidate, query));

    // Every name contains the empty string, so the ladder has to place it - and Prefix is what the
    // quick switcher's "no query lists everything" already means.
    [Fact]
    public void An_empty_query_is_a_prefix_of_every_name() =>
        Assert.Equal(NameRank.Prefix, NameMatch.Rank("Résumé", ""));

    [Fact]
    public void An_exact_name_wins_over_a_folded_one_that_came_first()
    {
        string[] notes = ["Résumé.md", "Resume.md"];

        Assert.Equal("Resume.md", NameMatch.Best(notes, Path.GetFileNameWithoutExtension, "Resume"));
        Assert.Equal("Résumé.md", NameMatch.Best(notes, Path.GetFileNameWithoutExtension, "Résumé"));
    }

    [Fact]
    public void The_first_folded_match_is_taken_when_nothing_matches_exactly()
    {
        string[] notes = ["Jönköping.md", "Jonköping.md"];

        Assert.Equal("Jönköping.md", NameMatch.Best(notes, Path.GetFileNameWithoutExtension, "jonkoping"));
    }

    // A link names a note; it does not go looking for one that merely starts with what was typed.
    [Fact]
    public void Best_refuses_a_prefix_or_substring_match()
    {
        string[] notes = ["Résumé 2026.md", "Old Résumé.md"];

        Assert.Null(NameMatch.Best(notes, Path.GetFileNameWithoutExtension, "Résumé"));
    }

    [Fact]
    public void Best_is_null_when_nothing_matches() =>
        Assert.Null(NameMatch.Best(["Budget.md"], Path.GetFileNameWithoutExtension, "Résumé"));
}
