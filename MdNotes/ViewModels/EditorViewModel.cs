using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Markdig.Syntax;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.ViewModels;

public sealed partial class EditorViewModel : ObservableObject
{
    private readonly VaultService _vault;
    private readonly MarkdownRenderer _renderer;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _saveTimer = new();
    private bool _loading;
    private static readonly Regex WordRegex = new(@"\S+", RegexOptions.Compiled);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote), nameof(CurrentDirectory), nameof(ContentRoot),
        nameof(IsLooseFile), nameof(SaveMode), nameof(SaveStatus))]
    public partial string? CurrentPath { get; private set; }

    [ObservableProperty] public partial string Text { get; set; } = string.Empty;
    [ObservableProperty] public partial MarkdownDocument? PreviewDocument { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    public partial bool IsDirty { get; private set; }
    [ObservableProperty] public partial string? NoteName { get; private set; }
    [ObservableProperty] public partial string Breadcrumb { get; private set; } = string.Empty;
    [ObservableProperty] public partial int WordCount { get; private set; }

    /// <summary>
    /// Where the caret is, two-way bound to the editor. Owned here rather than left in the TextBox so
    /// that leaving a note can record it and returning can put it back - the control is recreated on
    /// every view-mode change and remembers nothing across one.
    /// </summary>
    [ObservableProperty] public partial int CaretIndex { get; set; }
    [ObservableProperty] public partial int CharCount { get; private set; }

    public bool HasNote => CurrentPath is not null;

    /// <summary>
    /// True for a file opened from outside the vault (the OS file handler). Such a file is not
    /// autosaved: it is usually not the user's own note, so writes stay explicit.
    /// </summary>
    public bool IsLooseFile =>
        CurrentPath is { } path && (_vault.RootPath is not { } root || !PathScope.IsSameOrUnder(path, root));

    /// <summary>
    /// How the open file is written. The user's setting, except for a loose file, which is always
    /// manual - that is the same rule as before, now expressed in the same vocabulary.
    /// </summary>
    public AutoSaveMode SaveMode => IsLooseFile ? AutoSaveMode.Manual : _settings.AutoSave;

    /// <summary>What the status bar says: a mode that will not write on its own must not claim it will.</summary>
    public string SaveStatus => !IsDirty
        ? "Saved"
        : SaveMode switch
        {
            AutoSaveMode.Manual => "Unsaved · Ctrl+S",
            AutoSaveMode.OnFocusChange => "Unsaved · saves when you leave",
            _ => "Editing…",
        };

    /// <summary>Folder of the open note; relative image paths in the preview resolve against it.</summary>
    public string? CurrentDirectory => CurrentPath is null ? null : Path.GetDirectoryName(CurrentPath);

    /// <summary>
    /// Folder the preview may read images from: the vault for a note inside it, the note's own folder
    /// for a loose file - which has no vault to be contained by. An image path that climbs out of this
    /// is not rendered, so a note cannot pull a picture in from anywhere on disk.
    /// </summary>
    public string? ContentRoot => IsLooseFile ? CurrentDirectory : _vault.RootPath;

    public EditorViewModel(VaultService vault, MarkdownRenderer renderer, ISettingsService settings, IDialogService dialogs)
    {
        _vault = vault;
        _renderer = renderer;
        _settings = settings;
        _dialogs = dialogs;
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RenderPreview(); };
        // A disk that refuses the write is a failure the user can act on, not a crash: guarded like Ctrl+S.
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _dialogs.Guard(Save); };
        RenderPreview();
    }

    /// <summary>
    /// Saves the current note (if any), then opens <paramref name="path"/>. A dirty loose file is
    /// left alone - the caller asks the user first.
    /// </summary>
    public void Load(string path)
    {
        SaveImplicitly();
        var text = _vault.Read(path);
        SetContent(path, text);
    }

    /// <summary>Closes the current note without saving. Used when the note was deleted or the vault changed.</summary>
    public void Discard() => SetContent(null, string.Empty);

    /// <summary>
    /// Points the editor at a new path after the note (or a parent folder) was renamed. The remembered
    /// position moves with it: the note is the same note, and landing back at the top because someone
    /// renamed a folder would be the kind of small wrongness nobody reports.
    /// </summary>
    public void Retarget(string newPath)
    {
        if (CurrentPath is { } old) _settings.NotePositions.Forget(old);
        CurrentPath = newPath;
        RememberPosition();
    }

    public void Save()
    {
        _saveTimer.Stop();
        if (!IsDirty || CurrentPath is null) return;
        _vault.Write(CurrentPath, Text);
        IsDirty = false;
    }

    /// <summary>
    /// The single gate for every write the user did not ask for. Manual mode writes only on Ctrl+S,
    /// so the caller asks instead - see <see cref="FlushUnsavedAsync"/>.
    /// </summary>
    public void SaveImplicitly()
    {
        if (SaveMode != AutoSaveMode.Manual) Save();
    }

    /// <summary>
    /// Manual mode (and a loose file, which is always manual) never writes on its own, so moving on
    /// with the buffer dirty would lose the edit silently. Every path that moves the editor off a note
    /// comes through here first: a note switch, a vault switch, the replace overlay, and closing the
    /// window. The dialog service is passed rather than held - asking is the caller's business, and
    /// the editor otherwise has nothing to do with the user interface.
    /// </summary>
    public async Task FlushUnsavedAsync(IDialogService dialogs)
    {
        if (SaveMode != AutoSaveMode.Manual || !IsDirty) return;
        if (await dialogs.ConfirmAsync($"Save changes to '{NoteName}'?")) Save();
    }

    /// <summary>Re-reads the save mode after the settings changed, so the status bar keeps up.</summary>
    public void NotifySaveModeChanged()
    {
        OnPropertyChanged(nameof(SaveMode));
        OnPropertyChanged(nameof(SaveStatus));
    }

    /// <summary>
    /// Re-reads everything derived from the vault root after the vault was switched. A note opened
    /// from the old vault is now outside the new one, so it becomes a loose file and saves manually -
    /// the point of saying so here is that the change is visible rather than silent.
    /// </summary>
    public void NotifyVaultChanged()
    {
        UpdateNoteLabels();
        OnPropertyChanged(nameof(IsLooseFile));
        OnPropertyChanged(nameof(ContentRoot));
        NotifySaveModeChanged();
    }

    public void Close()
    {
        _previewTimer.Stop();
        RememberPosition();
        SaveImplicitly();
    }

    partial void OnTextChanged(string value)
    {
        if (_loading) return;
        IsDirty = true;
        _previewTimer.Stop();
        _previewTimer.Start();
        _saveTimer.Stop();
        if (SaveMode != AutoSaveMode.AfterDelay) return;
        _saveTimer.Interval = TimeSpan.FromMilliseconds(_settings.AutoSaveDelayMs);
        _saveTimer.Start();
    }

    partial void OnCurrentPathChanged(string? value) => UpdateNoteLabels();

    /// <summary>The note's name and breadcrumb. Both depend on the vault root, so both are recomputed
    /// when the path changes and when the vault does.</summary>
    private void UpdateNoteLabels()
    {
        var value = CurrentPath;
        if (value is null)
        {
            NoteName = null;
            Breadcrumb = string.Empty;
        }
        else
        {
            // A loose file has no vault entry, so it is named by its file name - which is what
            // DisplayName falls back to anyway, and saying so here is cheaper than asking.
            NoteName = IsLooseFile ? Path.GetFileNameWithoutExtension(value) : _vault.DisplayName(value);

            if (_vault.RootPath is { } root && !IsLooseFile)
            {
                var relativePath = Path.GetRelativePath(root, value);
                // Remove .md extension
                if (relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath[..^3];
                }
                // Replace directory separators with " / "
                Breadcrumb = relativePath.Replace('\\', '/').Replace("/", " / ");
            }
            else
            {
                // Outside the vault there is nothing to be relative to, so show the
                // containing folder instead of a path full of '..' segments.
                var name = Path.GetFileNameWithoutExtension(value);
                var folder = Path.GetFileName(Path.GetDirectoryName(value));
                Breadcrumb = string.IsNullOrEmpty(folder) ? name : $"{folder} / {name}";
            }
        }
    }

    private void SetContent(string? path, string text)
    {
        _previewTimer.Stop();
        _saveTimer.Stop();
        RememberPosition();

        _loading = true;
        try
        {
            CurrentPath = path;
            Text = text;

            // After the text, or the binding clamps the caret against the note being replaced.
            CaretIndex = _settings.NotePositions.Restore(path, text.Length);
        }
        finally
        {
            _loading = false;
        }
        IsDirty = false;
        RenderPreview();
    }

    /// <summary>
    /// Records where the caret was in the note being left, and persists the set. Called when the editor
    /// moves off a note and when the window closes - not when the caret moves, which is every keystroke.
    /// </summary>
    public void RememberPosition()
    {
        if (CurrentPath is not { } path) return;

        _settings.NotePositions.Remember(path, CaretIndex);
        _settings.SaveNotePositions();
    }

    private void RenderPreview()
    {
        PreviewDocument = _renderer.Parse(Text);
        // Count, not Matches(..).Count: this runs every 250 ms while typing, and Matches builds one
        // Match object per word only to have them counted.
        WordCount = WordRegex.Count(Text);
        CharCount = Text.Length;
    }
}
