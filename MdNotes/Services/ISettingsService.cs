using System.ComponentModel;
using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// Per-user settings, shared by every window. It notifies because some of these are application-wide -
/// the theme is one property with one effect, so a window that stored its own copy would fight the
/// others for it. Consumers read the property; this is how they learn to read it again.
/// </summary>
public interface ISettingsService : INotifyPropertyChanged
{
    string? LastVaultPath { get; set; }
    ThemeMode ThemeMode { get; set; }
    ViewMode ViewMode { get; set; }
    AutoSaveMode AutoSave { get; set; }
    int AutoSaveDelayMs { get; set; }

    /// <summary>
    /// The note every new note starts from, relative to the vault root and '/'-separated.
    /// Null - the default - means a new note starts blank.
    /// </summary>
    string? NewNoteTemplatePath { get; set; }

    /// <summary>
    /// Whether the open vault's own root '.gitignore' counts as an exclusion list as well as
    /// '.mdnotesignore'. Off by default: a repo hides build output, but it may also hide notes.
    /// </summary>
    bool UseGitIgnore { get; set; }

    /// <summary>
    /// Whether a note is shown under the title it declares - a YAML 'title:' or its first heading -
    /// instead of its file name. Display only: wikilinks still resolve by file name, and renaming still
    /// renames the file. Off by default, because turning it on means reading every note to build the
    /// tree, and because a vault whose file names are already its titles gains nothing.
    /// </summary>
    bool UseNoteTitles { get; set; }

    /// <summary>
    /// Where the caret was in each note, so reopening one lands where it was left. Persisted like the
    /// rest, because the value of it is mostly across restarts; bounded by <see cref="NotePositions"/>
    /// so a long-lived vault cannot grow an entry per note forever. Mutated in place and saved by
    /// <see cref="SaveNotePositions"/>, rather than being a setter, because the caller edits one entry
    /// at a time and rewriting the file per edit is what the explicit save exists to avoid.
    /// </summary>
    NotePositions NotePositions { get; }

    /// <summary>Writes the remembered positions out. Called when leaving a note, not when the caret moves.</summary>
    void SaveNotePositions();
}
