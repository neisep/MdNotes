using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.ViewModels;

/// <summary>
/// The settings overlay. Changes are written through to <see cref="ISettingsService"/> as they are
/// made - theme and view mode already apply instantly, so there is nothing to apply or cancel here.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly INoteTemplateService _templates;
    private readonly IVaultIgnoreService _exclusions;
    private readonly VaultService _vault;
    private readonly Action _changed;

    [ObservableProperty] public partial bool IsOpen { get; set; }

    public SettingsViewModel(ISettingsService settings, INoteTemplateService templates,
        IVaultIgnoreService exclusions, VaultService vault, Action changed)
    {
        _settings = settings;
        _templates = templates;
        _exclusions = exclusions;
        _vault = vault;
        _changed = changed;
    }

    /// <summary>
    /// Whether a note is shown under the title it declares. Turning it on makes the vault read every
    /// note to build the tree, so the refresh is explicit: nothing on disk changed, and without it the
    /// sidebar would keep showing file names until something else happened to invalidate it.
    /// </summary>
    public bool UseNoteTitles
    {
        get => _settings.UseNoteTitles;
        set
        {
            if (value == _settings.UseNoteTitles) return;

            _settings.UseNoteTitles = value;
            OnPropertyChanged();
            _vault.Refresh();
        }
    }

    /// <summary>Vault-relative. Empty means a new note starts blank, as it always did.</summary>
    public string TemplatePath
    {
        get => _settings.NewNoteTemplatePath ?? string.Empty;
        set
        {
            var normalized = NoteTemplate.NormalizePath(value);
            if (normalized == _settings.NewNoteTemplatePath) return;
            _settings.NewNoteTemplatePath = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TemplateStatus));
        }
    }

    /// <summary>
    /// What will actually happen, said at the moment of configuring. A missing template is silent when a
    /// note is created - an error dialog per note would be intolerable - so this line is where it shows.
    /// </summary>
    public string TemplateStatus => _templates.Probe() switch
    {
        TemplateState.Ready => $"Using {_settings.NewNoteTemplatePath}.",
        TemplateState.Missing => "No note at that path yet - new notes start blank until there is one.",
        TemplateState.Unreadable => "That file cannot be read - new notes start blank.",
        _ => "New notes start blank.",
    };

    /// <summary>
    /// The '.mdnotesignore' as written, one pattern per line. Committed on focus loss rather than per
    /// keystroke: every write rebuilds the tree in every window.
    /// </summary>
    public string ExcludedPatterns
    {
        get => _exclusions.Text;
        set
        {
            if (value == _exclusions.Text) return;

            _exclusions.Save(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExcludeStatus));
        }
    }

    public bool UseGitIgnore
    {
        get => _exclusions.UseGitIgnore;
        set
        {
            if (value == _exclusions.UseGitIgnore) return;

            _exclusions.UseGitIgnore = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExcludeStatus));
        }
    }

    /// <summary>
    /// What the list is actually doing, said where it is configured. The '!' count matters: a
    /// '.gitignore' of "*" and "!src/" means "keep src" to git and "hide everything" here.
    /// </summary>
    public string ExcludeStatus
    {
        get
        {
            var status = _exclusions.Probe();
            var line = status.State switch
            {
                IgnoreState.Active => $"{Count(status.PatternCount, "pattern")}; {Count(status.IndexedNotes, "note")} indexed.",
                IgnoreState.Unreadable => "That list cannot be read - nothing is excluded.",
                _ => "Nothing is excluded.",
            };

            return status.SkippedNegations == 0
                ? line
                : $"{line} {Count(status.SkippedNegations, "'!' line")} skipped - exceptions are not supported.";
        }
    }

    private static string Count(int value, string noun) => $"{value} {noun}{(value == 1 ? "" : "s")}";

    public AutoSaveMode SaveMode => _settings.AutoSave;
    public bool IsAfterDelay => SaveMode == AutoSaveMode.AfterDelay;
    public bool IsOnFocusChange => SaveMode == AutoSaveMode.OnFocusChange;
    public bool IsManual => SaveMode == AutoSaveMode.Manual;

    /// <summary>The delay is only a delay in <see cref="AutoSaveMode.AfterDelay"/>.</summary>
    public bool IsDelayEnabled => IsAfterDelay;

    /// <summary>Stored in milliseconds, offered in seconds: nobody thinks about typing in milliseconds.</summary>
    public double DelaySeconds
    {
        get => _settings.AutoSaveDelayMs / 1000.0;
        set
        {
            var milliseconds = AutoSaveDelay.Clamp((int)Math.Round(value * 1000));
            if (milliseconds == _settings.AutoSaveDelayMs) return;
            _settings.AutoSaveDelayMs = milliseconds;
            OnPropertyChanged();
            _changed();
        }
    }

    [RelayCommand]
    public void Open()
    {
        // The template file may have been created, and the exclusion list edited, since the panel was
        // last shown - by another editor, or by another window.
        OnPropertyChanged(nameof(TemplatePath));
        OnPropertyChanged(nameof(TemplateStatus));
        OnPropertyChanged(nameof(ExcludedPatterns));
        OnPropertyChanged(nameof(UseGitIgnore));
        OnPropertyChanged(nameof(UseNoteTitles));
        OnPropertyChanged(nameof(ExcludeStatus));
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand]
    public void SetSaveMode(AutoSaveMode mode)
    {
        if (_settings.AutoSave == mode) return;
        _settings.AutoSave = mode;
        OnPropertyChanged(nameof(SaveMode));
        OnPropertyChanged(nameof(IsAfterDelay));
        OnPropertyChanged(nameof(IsOnFocusChange));
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IsDelayEnabled));
        _changed();
    }
}
