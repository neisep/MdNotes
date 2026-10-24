using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// Reads and writes the vault's '.mdnotesignore', and tells the vault when it has changed. Every write
/// ends in <see cref="VaultService.RefreshExclusions"/>, which raises VaultChanged and so reaches every
/// open window - the same path an external edit takes.
/// </summary>
public sealed class VaultIgnoreService(VaultService vault, ISettingsService settings) : IVaultIgnoreService
{
    /// <summary>
    /// Read every time rather than cached: the panel asks when it opens and when the text changes, and
    /// a cache here would show one window a list another window had already replaced.
    /// </summary>
    public string Text => vault.RootPath is { } root ? IgnoreFile.Read(root) ?? string.Empty : string.Empty;

    public bool UseGitIgnore
    {
        get => settings.UseGitIgnore;
        set
        {
            if (settings.UseGitIgnore == value) return;

            settings.UseGitIgnore = value;
            vault.RefreshExclusions();
        }
    }

    public void Save(string text)
    {
        if (vault.RootPath is not { } root) return;

        IgnoreFile.Write(root, text);
        vault.RefreshExclusions();
    }

    /// <summary>
    /// Counts what is indexed, never what is hidden: counting the hidden means walking node_modules on
    /// every panel open, which is the cost the exclusion list exists to avoid.
    /// </summary>
    public IgnoreStatus Probe()
    {
        if (vault.RootPath is not { } root)
            return new IgnoreStatus(IgnoreState.None, 0, 0, 0);

        var exclusions = vault.Exclusions;
        var state = IgnoreFile.Read(root) is null
            ? IgnoreState.Unreadable
            : exclusions.IsEmpty ? IgnoreState.None : IgnoreState.Active;

        return new IgnoreStatus(state, exclusions.PatternCount, exclusions.SkippedNegations,
            vault.GetNotes().Count);
    }
}
