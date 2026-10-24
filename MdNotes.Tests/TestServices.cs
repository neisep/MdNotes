using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// The service set a window is built from, with the real vault, search and replace over a temp folder
/// and a fake for everything that would reach the user or the OS. One place, because the set used to
/// be written out at fourteen sites: the ninth service was a fourteen-file edit that reached tests
/// about diagram caching and drag ordering. A test that needs one member different says
/// <c>TestServices.For(vault) with { Templates = ... }</c>.
/// </summary>
internal static class TestServices
{
    /// <summary>
    /// The common case. The vault's own settings are used, not a second instance: that is how
    /// AppSession wires production, and a window holding its own copy is the arrangement this app has
    /// already been bitten by. They also name this vault as the last one opened, so
    /// TryOpenInitialVaultAsync finds it instead of reaching for the folder picker.
    /// </summary>
    public static AppServices For(TempVault vault, IDialogService? dialogs = null)
    {
        vault.Settings.LastVaultPath = vault.Root;
        return For(vault.Service, vault.Settings, dialogs);
    }

    /// <summary>
    /// Over any vault service, for the one test that needs a vault the temp folder does not own - the
    /// loose-file path, which has to be able to say "no vault is open at all".
    /// </summary>
    public static AppServices For(VaultService vault, ISettingsService settings, IDialogService? dialogs = null) =>
        new(vault,
            new SearchService(vault),
            new ReplaceService(vault),
            settings,
            dialogs ?? new FakeDialogs(),
            new MarkdownRenderer(),
            new FakeFileAssociation(),
            new FakeTemplates(),
            new FakeExclusions(),
            new MermaiderDiagramRenderer());
}
