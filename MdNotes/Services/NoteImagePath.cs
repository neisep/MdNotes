namespace MdNotes.Services;

/// <summary>
/// Where an image in a note may be read from: under the given root, and nowhere else. GetFullPath runs
/// before the containment check because PathScope compares prefixes - un-normalised, "root/../../x"
/// starts with the root and would pass.
/// </summary>
public static class NoteImagePath
{
    /// <summary>The image as an existing full path inside <paramref name="root"/>, or null if it is not one.</summary>
    public static string? Resolve(string? baseDirectory, string? root, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || baseDirectory is null) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile) return null;

        var full = Path.GetFullPath(Path.Combine(baseDirectory, Uri.UnescapeDataString(url)));
        // No root given means contain to the note's own folder: the closed default, never "anywhere".
        // PathScope appends the separator itself, so the scope must not already end in one.
        var scope = Path.GetFullPath(root ?? baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PathScope.IsSameOrUnder(full, scope) && File.Exists(full) ? full : null;
    }
}
