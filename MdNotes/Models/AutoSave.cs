namespace MdNotes.Models;

/// <summary>
/// When the editor writes the open note. The default keeps the old behaviour; the other two exist
/// because something else may be watching the file - a git repo, a sync folder, a file watcher.
/// </summary>
public enum AutoSaveMode
{
    /// <summary>A debounced write after the typing stops.</summary>
    AfterDelay,

    /// <summary>Only when the note is left: another note, another vault, the window closing or losing focus.</summary>
    OnFocusChange,

    /// <summary>Only on Ctrl+S. Leaving a dirty note asks first.</summary>
    Manual,
}

/// <summary>Bounds for the autosave delay. settings.json is hand-editable, so the value is clamped.</summary>
public static class AutoSaveDelay
{
    public const int MinMs = 100;
    public const int MaxMs = 300_000;
    public const int DefaultMs = 750;

    public static int Clamp(int milliseconds) => Math.Clamp(milliseconds, MinMs, MaxMs);
}
