namespace MdNotes.Models;

/// <summary>
/// One folder or note as the vault found it. <paramref name="Name"/> is the file name and the entry's
/// identity - what wikilinks resolve by and what a rename changes. <paramref name="Title"/> is what the
/// note declares about itself and is null unless the vault was asked to read titles; it changes what is
/// shown and nothing else.
/// </summary>
public sealed record VaultEntry(
    string Name,
    string FullPath,
    bool IsFolder,
    IReadOnlyList<VaultEntry> Children,
    string? Title = null)
{
    /// <summary>What the sidebar, the switcher and the window title show: the declared title, else the file name.</summary>
    public string Display => Title ?? Name;
}
