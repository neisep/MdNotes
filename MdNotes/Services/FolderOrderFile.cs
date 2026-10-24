using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// A folder's manual child order, stored beside the files as a '.order' text file:
/// one child key per line, folders suffixed with '/' so a folder and a note cannot collide.
/// A folder without the file is alphabetical.
/// </summary>
internal static class FolderOrderFile
{
    public const string FileName = ".order";

    public static string Key(VaultEntry entry) => Key(entry.FullPath, entry.IsFolder);

    public static string Key(string fullPath, bool isFolder) =>
        Path.GetFileName(fullPath) + (isFolder ? "/" : string.Empty);

    /// <summary>Orders children by the file; entries it does not list keep their incoming order, last.</summary>
    public static IReadOnlyList<VaultEntry> Apply(string folder, IReadOnlyList<VaultEntry> children)
    {
        var keys = ReadKeys(folder);
        if (keys.Count == 0) return children;

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keys.Count; i++)
            rank.TryAdd(keys[i], i);

        // OrderBy is stable, so unlisted entries keep the alphabetical folders-then-notes order.
        return [.. children.OrderBy(c => rank.TryGetValue(Key(c), out var i) ? i : int.MaxValue)];
    }

    /// <summary>Writes the given order; names no longer present are pruned by construction.</summary>
    public static void Save(string folder, IEnumerable<VaultEntry> children) =>
        File.WriteAllLines(Path.Combine(folder, FileName), children.Select(Key));

    public static void Delete(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Renames one key in place, keeping its position. An alphabetical folder has nothing to update.</summary>
    public static void Rename(string folder, string oldKey, string newKey)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return;

        var lines = File.ReadAllLines(path);
        var index = Array.FindIndex(lines, l => string.Equals(l.Trim(), oldKey, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        lines[index] = newKey;
        File.WriteAllLines(path, lines);
    }

    public static bool Exists(string folder) => File.Exists(Path.Combine(folder, FileName));

    private static List<string> ReadKeys(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return [];
        return [.. File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0)];
    }
}
