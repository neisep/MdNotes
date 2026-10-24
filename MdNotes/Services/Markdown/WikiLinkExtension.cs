using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace MdNotes.Services.Markdown;

/// <summary>Turns [[Target]] and [[Target|Alias]] into links with href https://note.local/Target.</summary>
public sealed class WikiLinkExtension : IMarkdownExtension
{
    public const string Host = "note.local";
    public const string UrlPrefix = "https://" + Host + "/";

    /// <summary>The CSS class every wikilink carries.</summary>
    public const string LinkClass = "wikilink";

    /// <summary>The extra class a folder target carries, so the renderer can mark it without re-parsing the url.</summary>
    public const string FolderClass = "wikilink-folder";

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<WikiLinkParser>())
            pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new WikiLinkParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }

    public static string? TargetFromUrl(Uri uri) =>
        uri.Host == Host ? Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) : null;

    /// <summary>A trailing '/' marks a folder, the same way a folder is written in a '.order' file.</summary>
    public static bool IsFolderTarget(string target) => target.EndsWith('/');

    /// <summary>The target without its folder marker - also what a folder link reads as when it has no alias.</summary>
    public static string FolderName(string target) => target.TrimEnd('/');
}

public sealed class WikiLinkParser : InlineParser
{
    public WikiLinkParser() => OpeningCharacters = ['['];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.CurrentChar != '[' || slice.PeekChar() != '[') return false;

        var start = slice.Start;
        var end = slice.Text.IndexOf("]]", start + 2, StringComparison.Ordinal);
        if (end < 0 || end > slice.End) return false;

        var inner = slice.Text.Substring(start + 2, end - start - 2);
        if (inner.Length == 0 || inner.Contains('[') || inner.Contains(']')) return false;

        var pipe = inner.IndexOf('|');
        var target = (pipe >= 0 ? inner[..pipe] : inner).Trim();
        var isFolder = WikiLinkExtension.IsFolderTarget(target);
        // A folder link reads as its name, so [[Projects/]] renders "Projects" unless it names an alias.
        var label = (pipe >= 0 ? inner[(pipe + 1)..] : WikiLinkExtension.FolderName(target)).Trim();
        if (target.Length == 0 || (isFolder && WikiLinkExtension.FolderName(target).Length == 0)) return false;

        var position = processor.GetSourcePosition(start, out var line, out var column);
        var link = new LinkInline(WikiLinkExtension.UrlPrefix + Uri.EscapeDataString(target), target)
        {
            Span = new Markdig.Syntax.SourceSpan(position, position + (end + 2 - start) - 1),
            Line = line,
            Column = column,
            IsClosed = true,
        };
        link.GetAttributes().AddClass(WikiLinkExtension.LinkClass);
        if (isFolder) link.GetAttributes().AddClass(WikiLinkExtension.FolderClass);
        link.AppendChild(new LiteralInline(label.Length > 0 ? label : target));

        processor.Inline = link;
        slice.Start = end + 2;
        return true;
    }
}
