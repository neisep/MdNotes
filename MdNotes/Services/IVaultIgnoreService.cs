using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// The exclusion list as the settings panel sees it. Separate from <see cref="VaultService"/> for the
/// same reason the template service is: the vault applies the list, what the list says is the user's.
/// </summary>
public interface IVaultIgnoreService
{
    /// <summary>
    /// The list as written. "" when there is none, no vault is open, or it will not read.
    /// Never throws: a list that cannot be shown must not stop the panel opening.
    /// </summary>
    string Text { get; }

    /// <summary>Whether the open vault's own root '.gitignore' counts as well. Off by default.</summary>
    bool UseGitIgnore { get; set; }

    /// <summary>Replaces the list - deleting the file when it is blank - and rebuilds every open tree.</summary>
    void Save(string text);

    /// <summary>What the list is doing right now. For the settings panel.</summary>
    IgnoreStatus Probe();
}
