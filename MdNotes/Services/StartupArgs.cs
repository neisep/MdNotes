namespace MdNotes.Services;

/// <summary>Reads the command line MdNotes is launched with, e.g. by a double-click in Explorer.</summary>
public static class StartupArgs
{
    /// <summary>
    /// The file MdNotes was asked to open, or null when it was launched plain. Existence is not
    /// checked here: a path that is wrong should be reported, not silently dropped.
    /// </summary>
    public static string? FilePath(string[] args) =>
        args.Select(a => a.Trim())
            .FirstOrDefault(a => a.Length > 0 && !a.StartsWith('-'));
}
