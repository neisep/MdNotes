using System.Text;
using System.Text.RegularExpressions;
using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// The one place that turns a query into spans on a string. Preview and apply both go through it, so a
/// replace writes exactly what the user was shown. Built once per search: an invalid regex fails here.
/// </summary>
internal sealed class MatchFinder
{
    private const int ContextRadius = 60;

    private readonly SearchOptions _options;
    private readonly string _replacement;
    private readonly Regex? _regex;
    private readonly string _needle;
    private readonly StringComparison _comparison;

    public MatchFinder(SearchOptions options, string replacement)
    {
        _options = options;
        _replacement = replacement;

        if (options.UseRegex)
        {
            // \b(?:...)\b keeps the user's alternations intact; a bad pattern throws out of here.
            var pattern = options.WholeWord ? $@"\b(?:{options.Query})\b" : options.Query;
            var flags = RegexOptions.Multiline | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            _regex = new Regex(pattern, flags, TimeSpan.FromSeconds(2));
            _needle = string.Empty;
            _comparison = StringComparison.Ordinal;
        }
        else
        {
            // Folding is length-preserving, so an index into the folded text still addresses the original.
            _needle = options.MatchCase ? options.Query : DiacriticFolder.Fold(options.Query);
            _comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }
    }

    /// <summary>
    /// Line indexes built so far - one per searched text that had a match. Exposed so a test can see that
    /// a text with none never pays for one; most notes in a vault-wide scan have none.
    /// </summary>
    internal int LineIndexesBuilt { get; private set; }

    /// <summary>
    /// Every match in <paramref name="text"/>. <paramref name="folded"/> is its
    /// <see cref="DiacriticFolder"/> fold when the caller already holds one, as the content cache does;
    /// without it a literal, case-blind search folds the text itself.
    /// </summary>
    public IReadOnlyList<TextMatch> Find(string text, string? folded = null)
    {
        if (_options.Query.Length == 0) return [];
        return _regex is null ? FindLiteral(text, folded) : FindRegex(text);
    }

    /// <summary>Splices the matches into the text. The matches must not overlap.</summary>
    public static string Apply(string text, IReadOnlyList<TextMatch> matches)
    {
        if (matches.Count == 0) return text;

        var builder = new StringBuilder(text.Length);
        var at = 0;
        foreach (var match in matches.OrderBy(m => m.Offset))
        {
            if (match.Offset < at)
                throw new InvalidOperationException($"Overlapping replacements at offset {match.Offset}.");
            builder.Append(text, at, match.Offset - at);
            builder.Append(match.Replacement);
            at = match.Offset + match.Length;
        }
        builder.Append(text, at, text.Length - at);
        return builder.ToString();
    }

    private List<TextMatch> FindLiteral(string text, string? folded)
    {
        var haystack = _options.MatchCase ? text : folded ?? DiacriticFolder.Fold(text);
        var matches = new List<TextMatch>();
        Lines? lines = null;

        var from = 0;
        while (from + _needle.Length <= haystack.Length)
        {
            var at = haystack.IndexOf(_needle, from, _comparison);
            if (at < 0) break;
            if (!_options.WholeWord || IsWholeWord(haystack, at, _needle.Length))
                matches.Add(Describe(text, lines ??= Index(text), at, _needle.Length, _replacement));
            from = at + _needle.Length;
        }
        return matches;
    }

    private List<TextMatch> FindRegex(string text)
    {
        var matches = new List<TextMatch>();
        Lines? lines = null;
        foreach (Match match in _regex!.Matches(text))
        {
            // A zero-width pattern would splice forever and can never be "replaced" meaningfully.
            if (match.Length == 0) continue;
            matches.Add(Describe(text, lines ??= Index(text), match.Index, match.Length, match.Result(_replacement)));
        }
        return matches;
    }

    /// <summary>Built on the first match, not up front: it is a pass over the whole text.</summary>
    private Lines Index(string text)
    {
        LineIndexesBuilt++;
        return new Lines(text);
    }

    private static TextMatch Describe(string text, Lines lines, int offset, int length, string replacement)
    {
        var (number, prefix, suffix) = lines.Describe(offset, length);
        return new TextMatch(offset, length, text.Substring(offset, length), replacement, number, prefix, suffix);
    }

    private static bool IsWholeWord(string text, int offset, int length) =>
        (offset == 0 || !IsWordChar(text[offset - 1]))
        && (offset + length == text.Length || !IsWordChar(text[offset + length]));

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Line starts, so a match offset turns into a line number and the text either side of it.</summary>
    private sealed class Lines(string text)
    {
        private readonly List<int> _starts = Build(text);

        public (int Number, string Prefix, string Suffix) Describe(int offset, int length)
        {
            var index = _starts.BinarySearch(offset);
            if (index < 0) index = ~index - 1;

            var start = _starts[index];
            var end = index + 1 < _starts.Count ? _starts[index + 1] : text.Length;
            while (end > start && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;

            // A match spanning lines shows only the part on its first line.
            var matchEnd = Math.Min(offset + length, end);
            var prefix = text[start..offset];
            var suffix = matchEnd < end ? text[matchEnd..end] : string.Empty;

            return (index + 1, Head(prefix), Tail(suffix));
        }

        private static List<int> Build(string text)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n') starts.Add(i + 1);
            return starts;
        }

        private static string Head(string value) =>
            value.Length <= ContextRadius ? value : "…" + value[^ContextRadius..];

        private static string Tail(string value) =>
            value.Length <= ContextRadius ? value : value[..ContextRadius] + "…";
    }
}
