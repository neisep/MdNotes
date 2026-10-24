namespace MdNotes.Services;

/// <summary>
/// The vault's exclusion list on disk: '.mdnotesignore' at the root, and the root '.gitignore' when the
/// user has asked for it. The only code that knows those names. Nothing here throws - a list that will
/// not read means "nothing is excluded", never a broken vault.
/// </summary>
internal static class IgnoreFile
{
    public const string FileName = ".mdnotesignore";
    public const string GitFileName = ".gitignore";

    /// <summary>The list as written; "" when there is no file, null when there is one but it will not read.</summary>
    public static string? Read(string root) => TryRead(Path.Combine(root, FileName));

    public static string? ReadGit(string root) => TryRead(Path.Combine(root, GitFileName));

    /// <summary>Writes the list, deleting the file when nothing is left in it - an empty file is litter.</summary>
    public static void Write(string root, string text)
    {
        var path = Path.Combine(root, FileName);
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            File.WriteAllText(path, text.ReplaceLineEndings());
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Nothing to do: the panel reports what the list is actually doing, which is now unchanged.
        }
    }

    /// <summary>
    /// What tells a cached list it has gone stale. The vault watcher reports names, not content, so a
    /// list edited in another editor is only noticed by comparing this against the last one seen.
    /// </summary>
    public static IgnoreStamp StampOf(string root, bool useGitIgnore)
    {
        var (ticks, length) = StatOf(Path.Combine(root, FileName));
        var (gitTicks, gitLength) = useGitIgnore
            ? StatOf(Path.Combine(root, GitFileName))
            : (0L, 0L);

        return new IgnoreStamp(root, ticks, length, gitTicks, gitLength, useGitIgnore);
    }

    private static (long Ticks, long Length) StatOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (file.LastWriteTimeUtc.Ticks, file.Length) : (0L, 0L);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return (0L, 0L);
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            // A folder where the list should be is "there but unreadable", not "not there".
            if (!File.Exists(path) && !Directory.Exists(path)) return string.Empty;
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}

/// <summary>A cheap identity for the files a <see cref="MdNotes.Models.VaultIgnore"/> was compiled from.</summary>
internal readonly record struct IgnoreStamp(
    string Root,
    long Ticks,
    long Length,
    long GitTicks,
    long GitLength,
    bool UseGitIgnore);
