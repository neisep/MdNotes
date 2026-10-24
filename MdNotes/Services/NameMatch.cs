namespace MdNotes.Services;

/// <summary>How well a candidate name answers a query, best first. Each unfolded tier beats its
/// folded twin, which is what keeps an exact name ahead of one that only matches once accents are
/// folded away.</summary>
public enum NameRank
{
    Exact,
    ExactFolded,
    Prefix,
    PrefixFolded,
    Contains,
    ContainsFolded,
    None,
}

/// <summary>
/// The one rule for matching a name the user typed against the names in the vault: case-insensitive,
/// and an exact name always wins over an accent-folded one - so a vault holding both Resume.md and
/// Résumé.md resolves each to its own file rather than whichever the tie-break happened to reach.
/// Extracted because wikilink resolution and the quick switcher each had their own copy, and a
/// change to folding or tie-breaking in one would have left them disagreeing about which note a word
/// names. Folding itself is <see cref="DiacriticFolder"/>; this is only the ordering built on it.
/// </summary>
public static class NameMatch
{
    /// <summary>Where <paramref name="candidate"/> sits against <paramref name="query"/>.</summary>
    public static NameRank Rank(string candidate, string query) =>
        Equal(candidate, query)
            ? NameRank.Exact
            : Rank(candidate, DiacriticFolder.Fold(candidate), query, DiacriticFolder.Fold(query));

    /// <summary>
    /// The same rule with both folds supplied, for a caller ranking many candidates against one query:
    /// the quick switcher folds each name once per vault change and the query once per keystroke,
    /// rather than both on every comparison.
    /// </summary>
    public static NameRank Rank(string candidate, string foldedCandidate, string query, string foldedQuery)
    {
        if (Equal(candidate, query)) return NameRank.Exact;
        if (Equal(foldedCandidate, foldedQuery)) return NameRank.ExactFolded;

        if (StartsWith(candidate, query)) return NameRank.Prefix;
        if (StartsWith(foldedCandidate, foldedQuery)) return NameRank.PrefixFolded;

        if (Contains(candidate, query)) return NameRank.Contains;
        if (Contains(foldedCandidate, foldedQuery)) return NameRank.ContainsFolded;

        return NameRank.None;
    }

    /// <summary>
    /// The candidate <paramref name="name"/> names, or null. Equality only - a prefix or a substring
    /// is not what a link or a lookup means - and an exact match anywhere in the sequence beats a
    /// folded one that came earlier, which is why this cannot be a single FirstOrDefault.
    /// </summary>
    public static T? Best<T>(IEnumerable<T> candidates, Func<T, string> key, string name) where T : class
    {
        T? folded = null;

        foreach (var candidate in candidates)
        {
            switch (Rank(key(candidate), name))
            {
                case NameRank.Exact:
                    return candidate;
                case NameRank.ExactFolded when folded is null:
                    folded = candidate;
                    break;
            }
        }

        return folded;
    }

    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string candidate, string query) =>
        candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string candidate, string query) =>
        candidate.Contains(query, StringComparison.OrdinalIgnoreCase);
}
