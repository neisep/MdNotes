using System.ComponentModel;
using System.Runtime.CompilerServices;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Benchmarks;

/// <summary>Settings held in memory, so a benchmark never touches the user's real settings file.</summary>
internal sealed class BenchSettings : ISettingsService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _useNoteTitles;
    private bool _useGitIgnore;

    public string? LastVaultPath { get; set; }
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public ViewMode ViewMode { get; set; } = ViewMode.Split;
    public AutoSaveMode AutoSave { get; set; } = AutoSaveMode.AfterDelay;
    public int AutoSaveDelayMs { get; set; } = AutoSaveDelay.DefaultMs;
    public string? NewNoteTemplatePath { get; set; }
    public bool UseGitIgnore { get => _useGitIgnore; set => Set(ref _useGitIgnore, value); }
    public bool UseNoteTitles { get => _useNoteTitles; set => Set(ref _useNoteTitles, value); }
    public NotePositions NotePositions { get; } = new();

    public void SaveNotePositions() { }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
