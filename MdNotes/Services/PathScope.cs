namespace MdNotes.Services;

/// <summary>Containment tests between file-system paths, shared by the vault and the editor.</summary>
public static class PathScope
{
    /// <summary>True when <paramref name="path"/> is <paramref name="ancestor"/> itself or sits inside it.</summary>
    /// <remarks>
    /// Compared in place rather than against <c>ancestor + separator</c>, which allocated a string per
    /// call inside per-note and per-folder loops.
    /// </remarks>
    public static bool IsSameOrUnder(string path, string ancestor) =>
        path.Length == ancestor.Length
            ? string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase)
            : path.Length > ancestor.Length
              && path[ancestor.Length] == Path.DirectorySeparatorChar
              && path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
}
