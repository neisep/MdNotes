namespace MdNotes.Models;

/// <summary>
/// What a replace actually did. <paramref name="Snapshot"/> holds every changed note's text from before
/// the write, which is what an undo puts back; <paramref name="Skipped"/> names notes whose text on disk
/// no longer matched what was previewed, so they were left alone.
/// </summary>
public sealed record ReplaceReport(
    int NotesChanged,
    int Replacements,
    IReadOnlyList<string> Skipped,
    IReadOnlyDictionary<string, string> Snapshot);
