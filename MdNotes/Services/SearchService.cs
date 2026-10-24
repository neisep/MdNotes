using MdNotes.Models;

namespace MdNotes.Services;

public sealed class SearchService(VaultService vault) : ISearchService
{
    private const int SnippetRadius = 40;

    public IReadOnlyList<SearchHit> Search(string query, CancellationToken token = default)
    {
        query = query.Trim();
        if (query.Length == 0) return [];

        var needle = DiacriticFolder.Fold(query);

        var hits = new List<SearchHit>();
        // What the sidebar calls each note, so a result and its row read the same - named from one
        // snapshot, because asking per note re-checked the exclusion list on disk once per note.
        foreach (var (path, name) in vault.GetNamedNotes())
        {
            // Once per note rather than once per scan: a note is the unit of work here, and the reads
            // inside the loop are what make a scan long enough to be worth abandoning part-way.
            token.ThrowIfCancellationRequested();

            // The fold comes from the content cache, made once per read of the note rather than per scan.
            // Folding is length-preserving, so this index also addresses the unfolded content,
            // which is what Snippet slices - the snippet keeps its real diacritics.
            var (content, folded) = vault.ReadFolded(path);
            var index = folded.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
                hits.Add(new SearchHit(path, name, Snippet(content, index, needle.Length)));
            else if (DiacriticFolder.Fold(name).Contains(needle, StringComparison.OrdinalIgnoreCase)
                     || DiacriticFolder.Fold(Path.GetFileNameWithoutExtension(path))
                         .Contains(needle, StringComparison.OrdinalIgnoreCase))
                hits.Add(new SearchHit(path, name, string.Empty));
        }
        return hits;
    }

    private static string Snippet(string content, int index, int length)
    {
        var start = Math.Max(0, index - SnippetRadius);
        var end = Math.Min(content.Length, index + length + SnippetRadius);
        var text = content[start..end].ReplaceLineEndings(" ");
        return (start > 0 ? "..." : "") + text + (end < content.Length ? "..." : "");
    }
}
