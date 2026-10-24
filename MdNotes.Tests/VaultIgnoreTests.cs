using MdNotes.Models;

namespace MdNotes.Tests;

/// <summary>
/// The pattern language itself. Keys are vault-relative and '/'-separated, so none of this needs a
/// vault or a file - which is the point of keeping the matcher in Models.
/// </summary>
public sealed class VaultIgnoreTests
{
    private static VaultIgnore Parse(string patterns) => VaultIgnore.Parse(patterns);

    [Theory]
    // A bare name matches that entry anywhere in the vault, and nothing longer.
    [InlineData("node_modules", "node_modules", true)]
    [InlineData("node_modules", "src/app/node_modules", true)]
    [InlineData("node_modules", "node_modules2", false)]
    [InlineData("node_modules", "src/node_modules/index.md", false)]
    // A leading or inner slash anchors the pattern to the vault root.
    [InlineData("/docs", "docs", true)]
    [InlineData("/docs", "src/docs", false)]
    [InlineData("src/docs", "src/docs", true)]
    [InlineData("src/docs", "a/src/docs", false)]
    // '*' and '?' stay inside one name.
    [InlineData("*.tmp", "a.tmp", true)]
    [InlineData("*.tmp", "x/y/a.tmp", true)]
    [InlineData("*.tmp", "a.tmpx", false)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("a?c", "ab/c", false)]
    // '**' spans separators, including none at all.
    [InlineData("**/foo", "foo", true)]
    [InlineData("**/foo", "a/b/foo", true)]
    [InlineData("**/foo", "xfoo", false)]
    [InlineData("a/**/b", "a/b", true)]
    [InlineData("a/**/b", "a/x/y/b", true)]
    [InlineData("a/**/b", "b", false)]
    // As in git, 'logs/**' empties the folder but leaves it standing.
    [InlineData("logs/**", "logs/a.md", true)]
    [InlineData("logs/**", "logs", false)]
    // Case is folded, as everywhere else in the app.
    [InlineData("Archive", "archive", true)]
    public void A_pattern_matches_what_it_says(string pattern, string key, bool expected) =>
        Assert.Equal(expected, Parse(pattern).Matches(key, isFolder: false));

    /// <summary>A trailing slash is the only thing that makes a pattern care what it hit.</summary>
    [Fact]
    public void A_trailing_slash_means_folders_only()
    {
        var ignore = Parse("build/");

        Assert.True(ignore.Matches("build", isFolder: true));
        Assert.False(ignore.Matches("build", isFolder: false));
        Assert.True(ignore.Matches("src/build", isFolder: true));
    }

    [Fact]
    public void Comments_and_blank_lines_are_not_patterns()
    {
        var ignore = Parse("# node_modules\n\n   \nbuild");

        Assert.Equal(1, ignore.PatternCount);
        Assert.False(ignore.Matches("node_modules", isFolder: true));
        Assert.True(ignore.Matches("build", isFolder: true));
    }

    /// <summary>
    /// '!' would make "is this excluded" depend on the order the rules were written in. It is dropped,
    /// and counted so the settings panel can say a '.gitignore' is not being read the way git reads it.
    /// </summary>
    [Fact]
    public void Exceptions_are_skipped_and_counted()
    {
        var ignore = Parse("*.tmp\n!keep.tmp");

        Assert.Equal(1, ignore.PatternCount);
        Assert.Equal(1, ignore.SkippedNegations);
        Assert.True(ignore.Matches("keep.tmp", isFolder: false));
    }

    /// <summary>The walk never descends into an excluded folder, so Matches deliberately stops at the entry.</summary>
    [Fact]
    public void Matches_is_about_the_entry_and_IsIgnored_about_its_ancestors()
    {
        var ignore = Parse("build/");

        Assert.False(ignore.Matches("build/out/a.md", isFolder: false));
        Assert.True(ignore.IsIgnored("build/out/a.md", isFolder: false));
        Assert.False(ignore.IsIgnored("src/a.md", isFolder: false));
    }

    [Fact]
    public void Nothing_to_parse_ignores_nothing()
    {
        var ignore = VaultIgnore.Parse(null, string.Empty);

        Assert.True(ignore.IsEmpty);
        Assert.False(ignore.IsIgnored("node_modules/x.md", isFolder: false));
    }

    [Fact]
    public void Several_lists_are_unioned()
    {
        var ignore = VaultIgnore.Parse("node_modules", "*.tmp");

        Assert.Equal(2, ignore.PatternCount);
        Assert.True(ignore.Matches("node_modules", isFolder: true));
        Assert.True(ignore.Matches("a.tmp", isFolder: false));
    }
}
