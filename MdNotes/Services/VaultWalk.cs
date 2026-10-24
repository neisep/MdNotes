using System.IO.Enumeration;
using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// The only code that reads a vault folder off the disk. Hand-rolled rather than
/// SearchOption.AllDirectories, which cannot be stopped from descending - the point of excluding
/// node_modules is not walking it - and whose options skip nothing, which is why '.git' used to be
/// searched even though the tree never showed it.
///
/// One walk answers the tree, the notes and the folders, because the three are the same set seen
/// three ways: same '*.md' filter, same dot-folder rule, same exclusion test. They used to be three
/// separate walks, which is how they came to disagree about '.git' in the first place.
/// </summary>
internal static class VaultWalk
{
    /// <summary>What counts as a note. The one place that decides.</summary>
    public const string Extension = ".md";

    /// <summary>
    /// Everything in the vault as one walk saw it, stamped with what makes it reusable.
    /// <paramref name="readNote"/> is how a note's text is fetched when titles are wanted, and null
    /// when they are not - the walk does not decide whether to open files, it is told.
    /// </summary>
    public static VaultSnapshot Of(string root, VaultIgnore ignore, long revision, Func<string, string>? readNote)
    {
        var tree = Children(root, string.Empty, ignore, readNote);

        var notes = new List<string>();
        var folders = new List<string>();
        var displays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Flatten(tree, notes, folders, displays);

        return new VaultSnapshot(
            root, ignore, revision, readNote is not null, tree, [.. notes], [.. folders], displays);
    }

    /// <summary>
    /// One folder's children in their stored order, exclusions not applied: this is what '.order'
    /// means. Ordering before filtering is what keeps an excluded entry's place when a sibling is
    /// dragged. Deliberately not served from the snapshot - a move rewrites '.order' from fresh,
    /// unfiltered disk state, and an excluded sibling must keep its line and its place.
    /// </summary>
    public static IReadOnlyList<VaultEntry> Entries(string folder)
    {
        // One read of the directory, which also says whether '.order' is there. It used to be read
        // twice - EnumerateDirectories, then EnumerateFiles - with a third stat for '.order', per
        // folder per walk. What the two calls did is kept exactly: the same full-path strings, the
        // same Win32 '*.md' match with the platform's casing, nothing hidden skipped.
        var folders = new List<VaultEntry>();
        var notes = new List<VaultEntry>();
        var hasOrder = false;

        foreach (var (fullPath, name, isFolder) in new FileSystemEnumerable<(string, string, bool)>(
                     folder,
                     static (ref FileSystemEntry e) => (e.ToSpecifiedFullPath(), e.FileName.ToString(), e.IsDirectory),
                     Compatible))
        {
            if (isFolder)
            {
                if (!name.StartsWith('.')) folders.Add(new VaultEntry(name, fullPath, true, []));
            }
            else if (string.Equals(name, FolderOrderFile.FileName, NameComparison))
            {
                hasOrder = true;
            }
            else if (FileSystemName.MatchesWin32Expression(NotePattern, name, IgnoreCase))
            {
                notes.Add(new VaultEntry(Path.GetFileNameWithoutExtension(name), fullPath, false, []));
            }
        }

        // OrderBy, not List.Sort: it is stable, so two names equal but for case keep the order they
        // were read in, as they did before.
        IReadOnlyList<VaultEntry> sorted =
        [
            .. folders.OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase),
            .. notes.OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase),
        ];
        return hasOrder ? FolderOrderFile.Apply(folder, sorted) : sorted;
    }

    private const string NotePattern = "*" + Extension;

    /// <summary>What Directory.EnumerateFiles uses by default: hidden and system entries included, and
    /// an unreadable folder an error rather than silently empty.</summary>
    private static readonly EnumerationOptions Compatible = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    /// <summary>The platform's file-name casing, as MatchCasing.PlatformDefault and File.Exists see it.</summary>
    private static readonly bool IgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private static readonly StringComparison NameComparison =
        IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The tree: ordered children with excluded entries dropped and excluded folders never descended
    /// into. <paramref name="prefix"/> is the parent's relative key with its trailing '/', so a key
    /// costs a concatenation rather than a path computation per entry.
    /// </summary>
    private static IReadOnlyList<VaultEntry> Children(
        string folder, string prefix, VaultIgnore ignore, Func<string, string>? readNote)
    {
        var kept = new List<VaultEntry>();
        foreach (var entry in Entries(folder))
        {
            // Not entry.Name: a note's name has had its extension taken off, and '*.md' must still match.
            var key = prefix + Path.GetFileName(entry.FullPath);
            if (ignore.Matches(key, entry.IsFolder)) continue;

            kept.Add(entry.IsFolder
                ? entry with { Children = Children(entry.FullPath, key + "/", ignore, readNote) }
                : entry with { Title = TitleOf(entry.FullPath, readNote) });
        }

        // An array rather than the list: one snapshot hands this same instance to every caller.
        return [.. kept];
    }

    /// <summary>
    /// What a note calls itself, or null when titles were not asked for. A note that cannot be read is
    /// not an error here: the tree is structure, and a row falling back to its file name is the right
    /// answer for a file that is locked, half-written or gone since the directory was listed.
    /// </summary>
    private static string? TitleOf(string fullPath, Func<string, string>? readNote)
    {
        if (readNote is null) return null;

        try
        {
            return NoteTitle.From(readNote(fullPath));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The tree read top to bottom, which is the order the sidebar draws it in - so a search result
    /// list and the sidebar finally agree, and a duplicate note name resolves the same way twice.
    /// </summary>
    private static void Flatten(
        IReadOnlyList<VaultEntry> entries,
        List<string> notes,
        List<string> folders,
        Dictionary<string, string> displays)
    {
        foreach (var entry in entries)
        {
            if (!entry.IsFolder)
            {
                notes.Add(entry.FullPath);
                // Only the notes that declared one. With titles off this map stays empty, and with them
                // on it holds just the difference from the file name - which is what a lookup falls back to.
                if (entry.Title is { } title) displays[entry.FullPath] = title;
                continue;
            }

            folders.Add(entry.FullPath);
            Flatten(entry.Children, notes, folders, displays);
        }
    }
}

/// <summary>
/// What one walk found, plus the four things that have to still hold for it to be handed out again:
/// the same vault, the same exclusion list instance, no structural change since, and the same answer
/// to whether notes were read for their titles.
/// </summary>
internal sealed record VaultSnapshot(
    string Root,
    VaultIgnore Ignore,
    long Revision,
    bool Titles,
    IReadOnlyList<VaultEntry> Tree,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Folders,
    IReadOnlyDictionary<string, string> Displays);
