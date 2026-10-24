namespace MdNotes.Models;

/// <summary>How a find query is matched. <paramref name="FolderScope"/> null means the whole vault.</summary>
public sealed record SearchOptions(
    string Query,
    bool MatchCase = false,
    bool WholeWord = false,
    bool UseRegex = false,
    string? FolderScope = null);
