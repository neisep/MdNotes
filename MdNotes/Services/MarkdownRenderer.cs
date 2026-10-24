using Markdig;
using Markdig.Syntax;
using MdNotes.Services.Markdown;

namespace MdNotes.Services;

public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        // Not part of UseAdvancedExtensions. Without it '---' frontmatter parses as a rule plus a
        // setext heading, so a templated note shows its own metadata as a title.
        .UseYamlFrontMatter()
        .Use<WikiLinkExtension>()
        .Build();

    public MarkdownDocument Parse(string markdown) => Markdig.Markdown.Parse(markdown, _pipeline);
}
