using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MdNotes.Services;
using MdNotes.ViewModels;
using MdNotes.Views;

namespace MdNotes;

/// <summary>
/// One set of services for the process, one window per open subject. Windows share the vault, so
/// the file watcher and the settings file are opened once however many windows are up.
/// </summary>
internal sealed class AppSession
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings = new JsonSettingsService();

    /// <summary>The one set every window is built from. Made here, once, and never varied.</summary>
    private readonly AppServices _services;

    public AppSession(IClassicDesktopStyleApplicationLifetime desktop, IDialogService dialogs)
    {
        _desktop = desktop;
        _dialogs = dialogs;
        var vault = new VaultService(_settings);
        _services = new AppServices(
            vault,
            new SearchService(vault),
            new ReplaceService(vault),
            _settings,
            dialogs,
            new MarkdownRenderer(),
            OperatingSystem.IsWindows()
                ? new WindowsFileAssociationService()
                : new UnsupportedFileAssociationService(),
            new NoteTemplateService(vault, _settings),
            new VaultIgnoreService(vault, _settings),
            new MermaiderDiagramRenderer());

        // The theme is one application-wide variant, so one subscription drives it for every window.
        // Per-window subscriptions made the last window to be constructed the one that won.
        App.ApplyTheme(_settings.ThemeMode);
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISettingsService.ThemeMode)) App.ApplyTheme(_settings.ThemeMode);
        };

        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
        desktop.Exit += (_, _) => _services.Vault.Dispose();
    }

    /// <summary>
    /// Opens a window. With a path it shows that file, restoring the vault only if there already is
    /// one - opening a loose file must never demand that a vault be chosen first.
    /// </summary>
    public MainWindow NewWindow(string? filePath)
    {
        var vm = new MainViewModel(_services);
        var window = new MainWindow { DataContext = vm };
        window.Opened += async (_, _) => await OpenSubjectAsync(vm, window, filePath);

        _desktop.MainWindow ??= window;
        window.Show();
        window.Activate();
        return window;
    }

    private async Task OpenSubjectAsync(MainViewModel vm, Window window, string? filePath)
    {
        if (filePath is null)
        {
            // Plain launch: a vault is the whole point, so cancelling the picker closes the window.
            if (!await vm.TryOpenInitialVaultAsync()) window.Close();
            return;
        }

        try
        {
            vm.TryRestoreVault();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(ex.Message);
        }

        vm.OpenLooseFile(filePath);
    }
}
