namespace MdNotes.Models;

/// <summary>
/// The title a note declares in its own text: a YAML <c>title:</c> property, or failing that its first
/// heading. A display name only - the file name stays the note's identity, which is what wikilinks
/// resolve by and what a rename changes. Two notes are allowed to declare the same title; two notes
/// cannot share a path.
///
/// Deliberately reads only the top of the file. A title is declared before the note says anything, so
/// scanning further is work that can never find one - and it is the tree build that pays for it.
/// </summary>
public static class NoteTitle
{
    /// <summary>How far in a title can be declared. Generous for frontmatter, small next to a note.</summary>
    private const int Limit = 4096;

    /// <summary>
    /// The declared title, or null when the note declares none. Whitespace-only is none: a heading with
    /// nothing after the hashes names nothing, and an empty row in the sidebar is worse than the file name.
    /// </summary>
    public static string? From(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var head = text.Length > Limit ? text[..Limit] : text;
        var lines = head.ReplaceLineEndings("\n").Split('\n');

        return FromFrontMatter(lines) ?? FromFirstHeading(lines);
    }

    /// <summary>
    /// The <c>title:</c> of a frontmatter block, which must open on the very first line to be one.
    /// Scanned by hand rather than parsed: a YAML parser would be a dependency, and reaching past the
    /// closing fence to a <c>title:</c> in the body would be wrong however it was found.
    /// </summary>
    private static string? FromFrontMatter(string[] lines)
    {
        if (lines.Length == 0 || lines[0].TrimEnd() != "---") return null;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimEnd() is "---" or "...") return null;

            if (!line.StartsWith("title:", StringComparison.OrdinalIgnoreCase)) continue;

            // Nested keys are indented, so an unindented 'title:' is the document's own.
            var value = Unquote(line["title:".Length..].Trim());
            return value.Length > 0 ? value : null;
        }

        // No closing fence inside the limit: this is not a frontmatter block, it is a horizontal rule.
        return null;
    }

    private static string? FromFirstHeading(string[] lines)
    {
        var inFence = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // A '#' inside a code fence is a comment in some other language, not this note's heading.
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence || !trimmed.StartsWith('#')) continue;

            var text = trimmed.TrimStart('#');
            // '#Tag' is a tag, not a heading: ATX headings need a space after the hashes.
            if (text.Length > 0 && !char.IsWhiteSpace(text[0])) continue;

            // Trailing hashes are the closed-ATX form and are not part of the heading.
            var title = text.Trim().TrimEnd('#').Trim();
            if (title.Length > 0) return title;
        }

        return null;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1].Trim()
            : value;
}
