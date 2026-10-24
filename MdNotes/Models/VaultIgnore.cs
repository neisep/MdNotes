using System.Text;
using System.Text.RegularExpressions;

namespace MdNotes.Models;

/// <summary>
/// A compiled exclusion list: what '.mdnotesignore' - and, if asked, '.gitignore' - say, as a test over
/// vault-relative, '/'-separated keys. A gitignore subset: no '!' exceptions and no '[abc]' classes,
/// because a rule that can un-hide things turns "is this note indexed" into a question about ordering.
/// Pure text; finding and reading the files belongs to the service.
/// </summary>
public sealed class VaultIgnore
{
    /// <summary>A bare name with no wildcards, matched against an entry's own name at any depth.</summary>
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folderNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Regex Pattern, bool FoldersOnly)> _patterns = [];

    private VaultIgnore()
    {
    }

    public static VaultIgnore Empty { get; } = new();

    /// <summary>How many usable patterns were found. Comments, blank lines and '!' lines do not count.</summary>
    public int PatternCount => _names.Count + _folderNames.Count + _patterns.Count;

    /// <summary>
    /// How many '!' lines were dropped. Worth saying out loud: a '.gitignore' of "*" plus "!src/" means
    /// "keep src" to git and "hide everything" here.
    /// </summary>
    public int SkippedNegations { get; private set; }

    public bool IsEmpty => PatternCount == 0;

    /// <summary>Unions several lists; a null or empty one contributes nothing.</summary>
    public static VaultIgnore Parse(params string?[] texts)
    {
        var ignore = new VaultIgnore();
        foreach (var text in texts)
            ignore.AddAll(text);
        return ignore.IsEmpty && ignore.SkippedNegations == 0 ? Empty : ignore;
    }

    /// <summary>
    /// True when this entry itself matches a pattern. Ancestors are deliberately not considered: the
    /// walk never descends into an excluded folder, so it has already tested every one of them, and
    /// keeping this exact is what lets "build/" hide a folder without hiding a file of the same name.
    /// </summary>
    public bool Matches(string relativeKey, bool isFolder)
    {
        if (IsEmpty || relativeKey.Length == 0) return false;

        var name = relativeKey[(relativeKey.LastIndexOf('/') + 1)..];
        if (_names.Contains(name)) return true;
        if (isFolder && _folderNames.Contains(name)) return true;

        foreach (var (pattern, foldersOnly) in _patterns)
            if ((!foldersOnly || isFolder) && pattern.IsMatch(relativeKey))
                return true;

        return false;
    }

    /// <summary>
    /// True when the entry, or any folder above it inside the vault, matches - for a path that did not
    /// come from the walk: the create guard and the file-system watcher.
    /// </summary>
    public bool IsIgnored(string relativeKey, bool isFolder)
    {
        if (IsEmpty) return false;

        for (var slash = relativeKey.IndexOf('/'); slash >= 0; slash = relativeKey.IndexOf('/', slash + 1))
            if (Matches(relativeKey[..slash], isFolder: true))
                return true;

        return Matches(relativeKey, isFolder);
    }

    private void AddAll(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            Add(line.Trim().TrimStart('﻿'));
    }

    private void Add(string line)
    {
        if (line.Length == 0 || line.StartsWith('#')) return;
        if (line.StartsWith('!'))
        {
            SkippedNegations++;
            return;
        }

        // A trailing slash means "folders only"; it is not what decides where the pattern is rooted.
        var foldersOnly = line.EndsWith('/');
        var pattern = line.TrimEnd('/');

        // Git's rule: any remaining slash - leading or inner - anchors the pattern to the vault root.
        var anchored = pattern.Contains('/');
        pattern = pattern.TrimStart('/');
        if (pattern.Length == 0) return;

        if (!anchored && pattern.IndexOfAny(['*', '?']) < 0)
        {
            (foldersOnly ? _folderNames : _names).Add(pattern);
            return;
        }

        // Not Compiled: the list is rebuilt whenever the file changes, so the JIT cost never pays back.
        _patterns.Add((new Regex(ToRegex(pattern, anchored),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), foldersOnly));
    }

    private static string ToRegex(string pattern, bool anchored)
    {
        var regex = new StringBuilder("^");
        if (!anchored) regex.Append("(?:.*/)?");

        var segments = pattern.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var last = i == segments.Length - 1;
            if (segments[i] == "**")
            {
                // '**' spans separators, so it supplies its own: "a/**/b" must still match "a/b".
                regex.Append(last ? ".+" : "(?:[^/]*/)*");
                continue;
            }

            foreach (var character in segments[i])
                regex.Append(character switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(character.ToString()),
                });

            if (!last) regex.Append('/');
        }

        return regex.Append('$').ToString();
    }
}

/// <summary>Whether the exclusion list can actually be used, for the settings panel to report.</summary>
public enum IgnoreState
{
    /// <summary>No list, or no vault to read one from: nothing is excluded.</summary>
    None,
    Active,
    Unreadable,
}

/// <summary>What the exclusion list is doing right now. For the settings panel.</summary>
public readonly record struct IgnoreStatus(
    IgnoreState State,
    int PatternCount,
    int SkippedNegations,
    int IndexedNotes);
