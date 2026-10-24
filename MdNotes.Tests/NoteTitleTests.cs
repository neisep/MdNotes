using MdNotes.Models;

namespace MdNotes.Tests;

/// <summary>
/// What a note declares as its title, and - more of these - what does not count as one. The rule runs
/// over every note in the vault on a tree build, so being wrong here is wrong in the sidebar.
/// </summary>
public class NoteTitleTests
{
    [Theory]
    [InlineData("---\ntitle: Q3 Planning\n---\n\n# Something else\n", "Q3 Planning")]
    [InlineData("---\ntitle: \"Quoted, with comma\"\n---\n", "Quoted, with comma")]
    [InlineData("---\ntitle: 'Single quoted'\n---\n", "Single quoted")]
    [InlineData("---\nTITLE: Case insensitive\n---\n", "Case insensitive")]
    [InlineData("---\ntags: [a]\ntitle: After another key\n---\n", "After another key")]
    public void The_frontmatter_title_wins(string text, string expected) =>
        Assert.Equal(expected, NoteTitle.From(text));

    [Theory]
    [InlineData("# Quarterly planning\n\nbody", "Quarterly planning")]
    [InlineData("\n\n## Deeper is still the first heading\n", "Deeper is still the first heading")]
    [InlineData("Some prose first.\n\n# Then a heading\n", "Then a heading")]
    [InlineData("# Closed ATX form ###\n", "Closed ATX form")]
    [InlineData("#    Extra spaces   \n", "Extra spaces")]
    public void The_first_heading_is_the_fallback(string text, string expected) =>
        Assert.Equal(expected, NoteTitle.From(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Just prose, no heading at all.")]
    [InlineData("#Tag not a heading\n")]                        // ATX needs a space after the hashes
    [InlineData("#\n")]                                          // hashes naming nothing
    [InlineData("---\ntitle:   \n---\n")]                        // declared empty
    [InlineData("```\n# Inside a fence\n```\n")]                 // a comment in some other language
    [InlineData("~~~\n# Inside a tilde fence\n~~~\n")]
    public void Nothing_declared_is_null(string? text) =>
        Assert.Null(NoteTitle.From(text));

    /// <summary>
    /// A rule with no closing fence is not frontmatter, and the heading under it is still the title -
    /// this is the case that would otherwise swallow a whole note looking for a '---'.
    /// </summary>
    [Fact]
    public void A_horizontal_rule_is_not_frontmatter() =>
        Assert.Equal("Real heading", NoteTitle.From("---\n\nsome prose\n\n# Real heading\n"));

    /// <summary>A 'title:' after the block has closed belongs to the body, not to the document.</summary>
    [Fact]
    public void A_title_key_past_the_closing_fence_is_not_the_title() =>
        Assert.Equal("The heading", NoteTitle.From("---\ntags: [a]\n---\n\ntitle: not this\n\n# The heading\n"));

    /// <summary>Only the top of the file is scanned, so a heading far down is not the note's title.</summary>
    [Fact]
    public void A_heading_past_the_scanned_head_is_not_found() =>
        Assert.Null(NoteTitle.From(new string('x', 5000) + "\n\n# Too far down\n"));

    [Fact]
    public void Windows_line_endings_read_the_same() =>
        Assert.Equal("Q3 Planning", NoteTitle.From("---\r\ntitle: Q3 Planning\r\n---\r\n"));
}
