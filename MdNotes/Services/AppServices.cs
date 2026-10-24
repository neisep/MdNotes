namespace MdNotes.Services;

/// <summary>
/// The one set of services a window is built from. Process-wide: <c>AppSession</c> makes it once and
/// hands every window the same instance, so the vault, the settings file and the watcher are opened
/// once however many windows are up.
///
/// A record rather than nine parameters because the nine were written out verbatim at fourteen sites:
/// adding the ninth service was a fourteen-file edit that reached tests about diagram caching and drag
/// ordering. The tenth, <see cref="Diagrams"/>, was one field here - which is what it was for. A test
/// that needs one member different says <c>services with { Dialogs = ... }</c> instead of restating
/// the set.
/// </summary>
public sealed record AppServices(
    VaultService Vault,
    ISearchService Search,
    ReplaceService Replace,
    ISettingsService Settings,
    IDialogService Dialogs,
    MarkdownRenderer Renderer,
    IFileAssociationService Associations,
    INoteTemplateService Templates,
    IVaultIgnoreService Exclusions,
    IDiagramRenderer Diagrams);
