using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdNotes.Services;
using MdNotes.Services.Markdown;

namespace MdNotes.Tests;

public class WikiLinkExtensionTests
{
    private readonly MarkdownRenderer _renderer = new();

    private LinkInline[] Links(string markdown) => _renderer.Parse(markdown).Descendants<LinkInline>().ToArray();

    private static string Label(LinkInline link) => string.Concat(link.OfType<LiteralInline>().Select(l => l.Content.ToString()));

    [Fact]
    public void Simple_wikilink_becomes_note_local_link()
    {
        var link = Assert.Single(Links("See [[Target]] here"));

        Assert.Equal("https://note.local/Target", link.Url);
        Assert.Contains("wikilink", link.GetAttributes().Classes!);
        Assert.Equal("Target", Label(link));
    }

    [Fact]
    public void Alias_is_used_as_label()
    {
        var link = Assert.Single(Links("[[Target|Shown text]]"));

        Assert.Equal("https://note.local/Target", link.Url);
        Assert.Equal("Shown text", Label(link));
    }

    [Fact]
    public void Spaces_are_escaped_in_url_and_round_trip_through_TargetFromUrl()
    {
        var link = Assert.Single(Links("[[My Note]]"));

        Assert.Equal("https://note.local/My%20Note", link.Url);
        Assert.Equal("My Note", WikiLinkExtension.TargetFromUrl(new Uri(link.Url!)));
    }

    [Fact]
    public void Trailing_text_is_not_swallowed_into_the_link()
    {
        var link = Assert.Single(Links("[[Target]] after"));

        Assert.Equal("Target", Label(link));
    }

    [Fact]
    public void Trailing_slash_marks_a_folder_target_and_is_dropped_from_the_label()
    {
        var link = Assert.Single(Links("See [[Projects/]] here"));

        Assert.Contains(WikiLinkExtension.FolderClass, link.GetAttributes().Classes!);
        Assert.Equal("Projects", Label(link));
        Assert.Equal("Projects/", WikiLinkExtension.TargetFromUrl(new Uri(link.Url!)));
    }

    [Fact]
    public void A_note_target_carries_no_folder_class()
    {
        var link = Assert.Single(Links("[[Projects]]"));

        Assert.DoesNotContain(WikiLinkExtension.FolderClass, link.GetAttributes().Classes!);
    }

    [Fact]
    public void A_path_form_folder_target_round_trips_through_the_escaped_url()
    {
        var link = Assert.Single(Links("[[Work/Projects/]]"));

        Assert.Equal("https://note.local/Work%2FProjects%2F", link.Url);
        Assert.Equal("Work/Projects/", WikiLinkExtension.TargetFromUrl(new Uri(link.Url!)));
        Assert.Equal("Work/Projects", Label(link));
    }

    [Fact]
    public void An_alias_survives_the_folder_marker()
    {
        var link = Assert.Single(Links("[[Projects/|the project]]"));

        Assert.Contains(WikiLinkExtension.FolderClass, link.GetAttributes().Classes!);
        Assert.Equal("the project", Label(link));
        Assert.Equal("Projects/", WikiLinkExtension.TargetFromUrl(new Uri(link.Url!)));
    }

    [Theory]
    [InlineData("[[]]")]
    [InlineData("[[a[b]]")]
    [InlineData("[[ | ]]")]
    [InlineData("[[/]]")]
    [InlineData("[[//]]")]
    public void Invalid_wikilinks_do_not_produce_links(string markdown)
    {
        Assert.DoesNotContain(Links(markdown), l => l.Url?.Contains("note.local") == true);
    }

    [Fact]
    public void Regular_links_and_other_hosts_are_untouched()
    {
        var link = Assert.Single(Links("[x](https://example.com)"));

        Assert.Equal("https://example.com", link.Url);
        Assert.Null(WikiLinkExtension.TargetFromUrl(new Uri("https://example.com/Target")));
    }
}
