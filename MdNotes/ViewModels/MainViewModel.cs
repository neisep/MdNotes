using System.ComponentModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.ViewModels;

/// <summary>
/// The window shell: view mode, theme, sidebar visibility, the title, the settings overlay and the
/// file-association offer. Everything vault-shaped - the tree, the selection, CRUD, navigation and
/// the sidebar search - lives in <see cref="Workspace"/>, a separate class because it changes for
/// separate reasons: a feature about notes lands there and never touches this file.
///
/// The editor stays here, not in the workspace: the open note belongs to the window (it survives a
/// vault switch as a loose file), and the status bar, the pane header and the empty state all read it
/// straight off the window. The workspace is handed it, because a rename or a delete has to move it.
///
/// IDisposable because one vault feeds every window, so a closed window must let go.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IFileAssociationService _associations;
    private readonly IDialogService _dialogs;
    private readonly PropertyChangedEventHandler _settingsChanged;
    private bool _loading;

    public EditorViewModel Editor { get; }

    /// <summary>
    /// Renders the preview's Mermaid fences. It reaches the view through a binding because a
    /// renderer is a service like the other nine, and the control that needs it is built by XAML.
    /// </summary>
    public IDiagramRenderer Diagrams { get; }

    public SettingsViewModel Settings { get; }
    public VaultWorkspaceViewModel Workspace { get; }

    public bool IsEditVisible => ViewMode is ViewMode.Edit or ViewMode.Split;
    public bool IsPreviewVisible => ViewMode is ViewMode.Split or ViewMode.Reading;
    public bool IsSplit => ViewMode == ViewMode.Split;
    public bool IsEditOnly => ViewMode == ViewMode.Edit;
    public bool IsReadingOnly => ViewMode == ViewMode.Reading;
    public GridLength EditorColumnWidth => IsEditVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    public GridLength PreviewColumnWidth => IsPreviewVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditVisible), nameof(IsPreviewVisible), nameof(IsSplit), nameof(IsEditOnly), nameof(IsReadingOnly), nameof(EditorColumnWidth), nameof(PreviewColumnWidth))]
    public partial ViewMode ViewMode { get; set; }

    /// <summary>
    /// Application-wide, not per-window: one variant is applied to the whole app and one value is
    /// persisted, so the window reads the setting rather than keeping a copy that could disagree.
    /// </summary>
    public ThemeMode ThemeMode => _settings.ThemeMode;
    [ObservableProperty] public partial bool IsSidebarVisible { get; set; } = true;

    /// <summary>The window names the note it holds; without one it names the vault.</summary>
    public string WindowTitle => Editor.NoteName is { } note
        ? $"{note} - MdNotes"
        : Workspace.VaultName is { } vault ? $"MdNotes - {vault}" : "MdNotes";

    public MainViewModel(AppServices services)
    {
        _settings = services.Settings;
        _associations = services.Associations;
        _dialogs = services.Dialogs;
        Diagrams = services.Diagrams;

        Editor = new EditorViewModel(services.Vault, services.Renderer, services.Settings, services.Dialogs);
        Settings = new SettingsViewModel(
            services.Settings, services.Templates, services.Exclusions, services.Vault, Editor.NotifySaveModeChanged);
        Workspace = new VaultWorkspaceViewModel(services, Editor);

        _loading = true;
        try
        {
            ViewMode = _settings.ViewMode;
        }
        finally
        {
            _loading = false;
        }

        _settingsChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(ISettingsService.ThemeMode)) OnPropertyChanged(nameof(ThemeMode));
        };
        _settings.PropertyChanged += _settingsChanged;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.NoteName) or nameof(EditorViewModel.IsLooseFile))
            OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>The title names the vault when no note is open, so a vault switch has to reach it.</summary>
    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VaultWorkspaceViewModel.VaultName))
            OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>Detaches from the shared vault. One vault feeds every window, so a closed window must let go.</summary>
    public void Dispose()
    {
        _settings.PropertyChanged -= _settingsChanged;
        Editor.PropertyChanged -= OnEditorPropertyChanged;
        Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        Workspace.Dispose();
    }

    /// <summary>Reopens the last vault when there is one. Never prompts, so a file can open without a vault.</summary>
    public bool TryRestoreVault() => Workspace.TryRestoreVault();

    /// <summary>Opens a file that lives outside the vault, straight into the editor.</summary>
    public void OpenLooseFile(string path) => Workspace.OpenLooseFile(path);

    /// <summary>Opens the last vault, or asks for one. Returns false if the user cancelled.</summary>
    public Task<bool> TryOpenInitialVaultAsync() => Workspace.TryOpenInitialVaultAsync();

    /// <summary>Saves and shuts the editor down. Returns false if saving failed, so the window can stay open.</summary>
    public Task<bool> TryCloseAsync() => _dialogs.TryAsync(async () =>
    {
        await Editor.FlushUnsavedAsync(_dialogs);
        Editor.Close();
        return true;
    });

    [RelayCommand]
    private void Save() => _dialogs.Guard(Editor.Save);

    /// <summary>
    /// A save nobody asked for, such as on leaving the window. Guarded here rather than inside the
    /// editor's gate, because a note switch relies on that gate throwing to keep the unsaved buffer.
    /// </summary>
    public void SaveImplicitly() => _dialogs.Guard(Editor.SaveImplicitly);

    [RelayCommand]
    private void SetViewMode(ViewMode mode) => ViewMode = mode;

    [RelayCommand]
    private void ToggleEditReading() => ViewMode = ViewMode switch
    {
        ViewMode.Edit => ViewMode.Reading,
        ViewMode.Reading => ViewMode.Edit,
        ViewMode.Split => ViewMode.Reading,
        _ => ViewMode,
    };

    /// <summary>Only worth offering where it works and where MdNotes is not already the default.</summary>
    public bool CanRegisterFileAssociation => _associations.IsSupported && !_associations.IsDefault;

    /// <summary>Registers MdNotes as the Markdown handler and re-evaluates whether the offer still stands.</summary>
    [RelayCommand]
    private void RegisterFileAssociation() => _dialogs.Guard(() =>
    {
        _associations.Register();
        OnPropertyChanged(nameof(CanRegisterFileAssociation));
    });

    [RelayCommand]
    private void CycleTheme() => _settings.ThemeMode = ThemeMode switch
    {
        ThemeMode.System => ThemeMode.Light,
        ThemeMode.Light => ThemeMode.Dark,
        ThemeMode.Dark => ThemeMode.System,
        _ => ThemeMode,
    };

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [RelayCommand]
    private void OpenSettings() => _dialogs.Guard(Settings.Open);

    partial void OnViewModeChanged(ViewMode value)
    {
        if (_loading) return;
        _settings.ViewMode = value;
    }
}
