namespace MdNotes.Models;

/// <summary>Every occurrence found in one note.</summary>
public sealed record NoteMatches(string FullPath, string Name, string Folder, IReadOnlyList<TextMatch> Matches);

/// <summary>
/// One occurrence: where it sits, what is there now, what would replace it, and the rest of its line
/// either side, already trimmed for display so the view owns no logic.
/// </summary>
public sealed record TextMatch(
    int Offset,
    int Length,
    string Found,
    string Replacement,
    int LineNumber,
    string Prefix,
    string Suffix);
