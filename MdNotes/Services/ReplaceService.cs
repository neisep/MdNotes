using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>Vault-wide find and replace. Stateless: the undo snapshot travels in the report.</summary>
public sealed class ReplaceService(VaultService vault)
{
    /// <summary>Every occurrence in every note in scope, with the replacement each one would get.</summary>
    public IReadOnlyList<NoteMatches> Find(SearchOptions options, string replacement, CancellationToken token = default)
    {
        if (options.Query.Length == 0) return [];

        var root = vault.RootPath ?? throw new InvalidOperationException("No vault is open.");
        var scope = options.FolderScope ?? root;
        if (!Directory.Exists(scope))
            throw new DirectoryNotFoundException($"Folder does not exist: {scope}");

        var finder = new MatchFinder(options, replacement);
        var results = new List<NoteMatches>();

        foreach (var path in vault.GetNotes())
        {
            // Cancelled per note for the same reason SearchService.Search is: the overlay re-runs this
            // on every keystroke, and the run it is replacing has no reason to finish.
            token.ThrowIfCancellationRequested();

            // GetNotes already leaves out dot-folders and anything excluded.
            if (!PathScope.IsSameOrUnder(path, scope)) continue;

            var (text, folded) = vault.ReadFolded(path);
            var matches = finder.Find(text, folded);
            if (matches.Count == 0) continue;

            var folder = Path.GetDirectoryName(Path.GetRelativePath(root, path)) ?? string.Empty;
            results.Add(new NoteMatches(
                path,
                Path.GetFileNameWithoutExtension(path),
                folder.Replace(Path.DirectorySeparatorChar, '/'),
                matches));
        }

        return results;
    }

    /// <summary>
    /// Writes the given matches, which are the ones the user approved. A note whose text on disk no
    /// longer matches what was found is left untouched and named in the report.
    /// </summary>
    public ReplaceReport Replace(IReadOnlyList<NoteMatches> selection)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();
        var replacements = 0;

        foreach (var note in selection)
        {
            if (note.Matches.Count == 0) continue;

            var text = vault.Read(note.FullPath);
            if (!StillMatches(text, note.Matches))
            {
                skipped.Add(note.FullPath);
                continue;
            }

            snapshot[note.FullPath] = text;
            vault.Write(note.FullPath, MatchFinder.Apply(text, note.Matches));
            replacements += note.Matches.Count;
        }

        return new ReplaceReport(snapshot.Count, replacements, skipped, snapshot);
    }

    /// <summary>Writes a report's snapshot back, undoing the replace it came from.</summary>
    public void Restore(IReadOnlyDictionary<string, string> snapshot)
    {
        foreach (var (path, text) in snapshot)
            vault.Write(path, text);
    }

    /// <summary>The file has to still say what the preview said, or the whole note is left alone.</summary>
    private static bool StillMatches(string text, IReadOnlyList<TextMatch> matches)
    {
        foreach (var match in matches)
        {
            if (match.Offset < 0 || match.Offset + match.Length > text.Length) return false;
            if (!text.AsSpan(match.Offset, match.Length).SequenceEqual(match.Found)) return false;
        }
        return true;
    }
}
