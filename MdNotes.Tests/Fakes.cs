using System.ComponentModel;
using System.Runtime.CompilerServices;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>Notifies like the real one, so a test can watch what a setting change reaches.</summary>
internal sealed class FakeSettings : ISettingsService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string? _lastVaultPath;
    private ThemeMode _themeMode = ThemeMode.System;
    private ViewMode _viewMode = ViewMode.Split;
    private AutoSaveMode _autoSave = AutoSaveMode.AfterDelay;
    private int _autoSaveDelayMs = AutoSaveDelay.DefaultMs;
    private string? _newNoteTemplatePath;
    private bool _useGitIgnore;
    private bool _useNoteTitles;

    public string? LastVaultPath { get => _lastVaultPath; set => Set(ref _lastVaultPath, value); }
    public ThemeMode ThemeMode { get => _themeMode; set => Set(ref _themeMode, value); }
    public ViewMode ViewMode { get => _viewMode; set => Set(ref _viewMode, value); }
    public AutoSaveMode AutoSave { get => _autoSave; set => Set(ref _autoSave, value); }
    public int AutoSaveDelayMs { get => _autoSaveDelayMs; set => Set(ref _autoSaveDelayMs, value); }
    public string? NewNoteTemplatePath { get => _newNoteTemplatePath; set => Set(ref _newNoteTemplatePath, value); }
    public bool UseGitIgnore { get { GitIgnoreReads++; return _useGitIgnore; } set => Set(ref _useGitIgnore, value); }

    /// <summary>
    /// How many times the vault asked. It asks once per check of its exclusion files on disk, so this
    /// counts those checks without a seam in the product code.
    /// </summary>
    public int GitIgnoreReads { get; set; }
    public bool UseNoteTitles { get => _useNoteTitles; set => Set(ref _useNoteTitles, value); }

    public NotePositions NotePositions { get; } = new();

    /// <summary>How many times positions were saved, so a test can tell a save from a mere remember.</summary>
    public int PositionSaves { get; private set; }

    public void SaveNotePositions() => PositionSaves++;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>A template service with no vault behind it: whatever the test sets is what new notes get.</summary>
internal sealed class FakeTemplates : INoteTemplateService
{
    public string Content { get; set; } = string.Empty;
    public TemplateState State { get; set; } = TemplateState.None;

    public string ContentFor(string folderPath, string name) => Content;
    public TemplateState Probe() => State;
}

/// <summary>An exclusion list with no vault behind it: whatever the test sets is what the panel sees.</summary>
internal sealed class FakeExclusions : IVaultIgnoreService
{
    public string Text { get; set; } = string.Empty;
    public bool UseGitIgnore { get; set; }
    public IgnoreStatus Status { get; set; } = new(IgnoreState.None, 0, 0, 0);

    /// <summary>How many times the list was written, so a no-op can be told from a write of the same text.</summary>
    public int Saves { get; private set; }

    public void Save(string text)
    {
        Text = text;
        Saves++;
    }

    public IgnoreStatus Probe() => Status;
}

/// <summary>Records what the view model tried to show instead of opening a window.</summary>
internal sealed class FakeDialogs : IDialogService
{
    public List<string> Errors { get; } = [];
    public List<string> Confirmations { get; } = [];

    /// <summary>What the user "answers" to ConfirmAsync; tests flip it to decline.</summary>
    public bool ConfirmResult { get; set; } = true;

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string message)
    {
        Confirmations.Add(message);
        return Task.FromResult(ConfirmResult);
    }
    public Task<string?> PromptTextAsync(string title, string initialValue) => Task.FromResult<string?>(initialValue);

    /// <summary>What the folder picker "returns"; null is the user cancelling, which is the default.</summary>
    public string? PickFolderResult { get; set; }

    public Task<string?> PickFolderAsync() => Task.FromResult(PickFolderResult);
}

/// <summary>No OS registration in tests; the offer is simply never available.</summary>
internal sealed class FakeFileAssociation : IFileAssociationService
{
    public bool IsSupported => false;
    public bool IsDefault => false;
    public void Register() => throw new NotSupportedException();
}

/// <summary>Fails the test loudly: a run whose point is that nothing broke must not swallow an error.</summary>
internal sealed class ThrowingDialogs : IDialogService
{
    public Task ShowErrorAsync(string message) => throw new InvalidOperationException(message);
    public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);
    public Task<string?> PromptTextAsync(string title, string initialValue) => Task.FromResult<string?>(initialValue);
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
}
