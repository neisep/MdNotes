using MdNotes.Models;

namespace MdNotes.Tests;

/// <summary>
/// The pure half of the feature: placeholder substitution and what counts as a usable template path.
/// The clock is an argument, so none of this needs a UI thread or a vault.
/// </summary>
public sealed class NoteTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 14, 5, 0, TimeSpan.Zero);

    private static string Apply(string template, string title = "Meeting") =>
        NoteTemplate.Apply(template, title, Now);

    [Fact]
    public void Fills_title_date_and_time()
    {
        var result = Apply("---\ncreated: {{date}} {{time}}\n---\n\n# {{title}}\n");

        Assert.Equal("---\ncreated: 2026-09-18 14:05\n---\n\n# Meeting\n", result);
    }

    [Theory]
    [InlineData("{{TITLE}}", "Meeting")]
    [InlineData("{{ date }}", "2026-09-18")]
    [InlineData("{{Time}}", "14:05")]
    public void Names_are_case_insensitive_and_may_be_padded(string template, string expected) =>
        Assert.Equal(expected, Apply(template));

    [Theory]
    [InlineData("{{date:yyyy/MM}}", "2026/09")]
    [InlineData("{{date:dd MMMM yyyy}}", "18 September 2026")]
    [InlineData("{{time:HH-mm}}", "14-05")]
    public void An_explicit_format_is_honoured(string template, string expected) =>
        Assert.Equal(expected, Apply(template));

    [Theory]
    [InlineData("{{dataview}}")]
    [InlineData("{{#each items}}")]
    [InlineData("{{ user.name }}")]
    [InlineData("{{}}")]
    [InlineData("{ {title} }")]
    [InlineData("{{title:x}}")]
    public void Anything_it_does_not_know_is_left_exactly_as_written(string template) =>
        Assert.Equal(template, Apply(template));

    [Fact]
    public void A_format_the_framework_rejects_is_left_alone_rather_than_throwing() =>
        Assert.Equal("{{date:\"}}", Apply("{{date:\"}}"));

    /// <summary>Chained Replace would re-scan its own output and expand the date the title carried in.</summary>
    [Fact]
    public void A_title_that_contains_a_placeholder_is_not_substituted_again() =>
        Assert.Equal("# {{date}}", Apply("# {{title}}", title: "{{date}}"));

    [Fact]
    public void An_empty_template_stays_empty() => Assert.Equal("", Apply(""));

    [Theory]
    [InlineData(" Templates\\Daily.md ", "Templates/Daily.md")]
    [InlineData("Templates/Daily", "Templates/Daily.md")]
    [InlineData("/Templates/Daily.md", "Templates/Daily.md")]
    [InlineData("Daily.MD", "Daily.MD")]
    public void A_usable_path_is_normalised(string setting, string expected) =>
        Assert.Equal(expected, NoteTemplate.NormalizePath(setting));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../secret.md")]
    [InlineData("Templates/../../secret.md")]
    [InlineData("C:\\secret.md")]
    public void Anything_that_would_leave_the_vault_or_names_nothing_is_no_template(string? setting) =>
        Assert.Null(NoteTemplate.NormalizePath(setting));
}
