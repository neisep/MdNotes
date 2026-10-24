using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MdNotes.Models;

namespace MdNotes.Services;

public sealed partial class JsonSettingsService : ISettingsService
{
    /// <summary>
    /// Source-generated rather than reflection-based: the first read happens at startup, before any
    /// window, and the reflection path's warm-up measured 43 ms against 32 ms for this one. Default
    /// options either way, so the file on disk reads and writes exactly as it did.
    /// </summary>
    [JsonSerializable(typeof(Data))]
    private sealed partial class DataJson : JsonSerializerContext;

    private sealed class Data
    {
        public string? LastVaultPath { get; set; }
        public bool UseNoteTitles { get; set; }
        public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
        public ViewMode ViewMode { get; set; } = ViewMode.Split;
        public AutoSaveMode AutoSave { get; set; } = AutoSaveMode.AfterDelay;
        public int AutoSaveDelayMs { get; set; } = AutoSaveDelay.DefaultMs;
        public string? NewNoteTemplatePath { get; set; }
        public bool UseGitIgnore { get; set; }
        public Dictionary<string, int> NotePositions { get; set; } = [];
    }

    private readonly string _file;
    private readonly Data _data;

    public JsonSettingsService() : this(DefaultFile()) { }

    /// <summary>Settings kept in <paramref name="file"/>. Internal so a test never touches the real one.</summary>
    internal JsonSettingsService(string file)
    {
        _file = file;
        _data = Read(_file);

        // Oldest first, which is the order they were written in and the order the bound drops them.
        foreach (var (path, caret) in _data.NotePositions) NotePositions.Remember(path, caret);
    }

    /// <summary>
    /// Runs before any window and before the crash barrier, so a file that cannot be read must not
    /// stop the app from starting: it starts on defaults instead. The unreadable file is moved aside
    /// first, because the next setting changed would otherwise overwrite it and whatever was in it
    /// could no longer be recovered by hand.
    /// </summary>
    private static Data Read(string file)
    {
        if (!File.Exists(file)) return new Data();
        try
        {
            var data = JsonSerializer.Deserialize(File.ReadAllText(file), DataJson.Default.Data) ?? new Data();
            data.NotePositions ??= [];
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Move(file, file + ".corrupt", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do: defaults still beat not starting.
            }
            return new Data();
        }
    }

    private static string DefaultFile()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdNotes");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public NotePositions NotePositions { get; } = new();

    public void SaveNotePositions()
    {
        _data.NotePositions = NotePositions.All.ToDictionary(e => e.Key, e => e.Value);
        Write();
    }

    public string? LastVaultPath
    {
        get => _data.LastVaultPath;
        set
        {
            _data.LastVaultPath = value;
            Write();
        }
    }

    public ThemeMode ThemeMode
    {
        get => _data.ThemeMode;
        set
        {
            _data.ThemeMode = value;
            Write();
        }
    }

    public ViewMode ViewMode
    {
        get => _data.ViewMode;
        set
        {
            _data.ViewMode = value;
            Write();
        }
    }

    public AutoSaveMode AutoSave
    {
        get => _data.AutoSave;
        set
        {
            _data.AutoSave = value;
            Write();
        }
    }

    /// <summary>Clamped on the way out as well as in: the file can be edited by hand.</summary>
    public int AutoSaveDelayMs
    {
        get => AutoSaveDelay.Clamp(_data.AutoSaveDelayMs);
        set
        {
            _data.AutoSaveDelayMs = AutoSaveDelay.Clamp(value);
            Write();
        }
    }

    /// <summary>Normalised on the way out as well as in, for the same reason the delay is clamped.</summary>
    public string? NewNoteTemplatePath
    {
        get => NoteTemplate.NormalizePath(_data.NewNoteTemplatePath);
        set
        {
            _data.NewNoteTemplatePath = NoteTemplate.NormalizePath(value);
            Write();
        }
    }

    public bool UseGitIgnore
    {
        get => _data.UseGitIgnore;
        set
        {
            _data.UseGitIgnore = value;
            Write();
        }
    }

    public bool UseNoteTitles
    {
        get => _data.UseNoteTitles;
        set
        {
            _data.UseNoteTitles = value;
            Write();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Write-through, then tell whoever is reading this property. The caller name is the
    /// property, so every setter is one call and none of them can name the wrong one.</summary>
    private void Write([CallerMemberName] string? property = null)
    {
        File.WriteAllText(_file, JsonSerializer.Serialize(_data, DataJson.Default.Data));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
