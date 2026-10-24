using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.ViewModels;

/// <summary>
/// The vault-wide search and replace overlay: options, the matches they find grouped by note, what the
/// user ticked, and the single undo step the last replace left behind.
/// </summary>
public sealed partial class SearchReplaceViewModel : ObservableObject
{
    private readonly ReplaceService _replace;
    private readonly VaultService _vault;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _openPath;
    private readonly Action<ReplaceReport> _applied;
    private readonly DispatcherTimer _rescanTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>
    /// Matches the user unticked, kept across rescans so refining the text keeps their choices - and
    /// forgotten on Close, so a later session starts from everything ticked rather than from choices
    /// made about a search nobody can see any more.
    /// </summary>
    private readonly HashSet<(string Path, int Offset)> _excluded = [];

    private IReadOnlyDictionary<string, string>? _undo;
    private bool _loading;

    /// <summary>The find in flight, cancelled by the next keystroke or option change.</summary>
    private CancellationTokenSource? _findCts;

    /// <summary>
    /// The notes found, replaced wholesale by each scan: one change notification per scan, where filling
    /// a bound collection row by row raised one per note, each with its own container and layout pass.
    /// </summary>
    [ObservableProperty] public partial IReadOnlyList<NoteResultViewModel> Results { get; private set; } = [];

    /// <summary>
    /// What the status line counts, kept as running totals: a checkbox click changes one note, and
    /// recounting every match in every note per click was the cost of a vault-wide result set.
    /// </summary>
    private int _selectedMatches;
    private int _selectedNotes;
    public ObservableCollection<ScopeItem> Folders { get; } = [];

    [ObservableProperty] public partial bool IsOpen { get; private set; }
    [ObservableProperty] public partial string Find { get; set; } = string.Empty;
    [ObservableProperty] public partial string Replacement { get; set; } = string.Empty;
    [ObservableProperty] public partial bool MatchCase { get; set; }
    [ObservableProperty] public partial bool WholeWord { get; set; }
    [ObservableProperty] public partial bool UseRegex { get; set; }
    [ObservableProperty] public partial ScopeItem? Scope { get; set; }
    [ObservableProperty] public partial string? Error { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool CanReplace { get; private set; }
    [ObservableProperty] public partial bool CanUndo { get; private set; }

    /// <summary>A folder the search can be limited to; a null path means the whole vault.</summary>
    public sealed record ScopeItem(string Label, string? Path);

    public SearchReplaceViewModel(
        ReplaceService replace,
        VaultService vault,
        IDialogService dialogs,
        Action<string> openPath,
        Action<ReplaceReport> applied)
    {
        _replace = replace;
        _vault = vault;
        _dialogs = dialogs;
        _openPath = openPath;
        _applied = applied;
        _rescanTimer.Tick += (_, _) => { _rescanTimer.Stop(); _ = RescanAsync(); };
    }

    /// <summary>Opens the overlay, seeded with a query and optionally limited to one folder.</summary>
    public Task OpenAsync(string? find, string? folderScope)
    {
        _loading = true;
        try
        {
            RefreshFolders(folderScope);
            if (!string.IsNullOrEmpty(find)) Find = find;
            Error = null;
        }
        finally
        {
            _loading = false;
        }

        IsOpen = true;
        return RescanAsync();
    }

    [RelayCommand]
    public void Close()
    {
        _rescanTimer.Stop();
        CancelFind();
        IsOpen = false;
        ShowResults([]);
        _excluded.Clear();
        UpdateStatus();
    }

    /// <summary>Forgets the undo step, e.g. when the vault is switched.</summary>
    public void ClearUndo()
    {
        _undo = null;
        CanUndo = false;
    }

    /// <summary>
    /// Rebuilds the results from disk. A bad pattern lands in Error, not in a dialog.
    ///
    /// The find reads every note in scope, so it runs on the thread pool: measured at 122 ms over two
    /// thousand notes and 319 ms over five thousand, and this is driven from a 250 ms debounce while
    /// someone types a pattern. The results are cleared when the new ones arrive rather than before the
    /// scan, so the list no longer blinks to empty - and "No matches" now only ever means no matches.
    /// </summary>
    public async Task RescanAsync()
    {
        _rescanTimer.Stop();
        CancelFind();

        if (!IsOpen || Find.Length == 0)
        {
            Replace([]);
            return;
        }

        _findCts = new CancellationTokenSource();
        var token = _findCts.Token;
        var options = new SearchOptions(Find, MatchCase, WholeWord, UseRegex, Scope?.Path);
        var replacement = Replacement;

        IReadOnlyList<NoteMatches> found;
        try
        {
            found = await Task.Run(() => _replace.Find(options, replacement, token), token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by the keystroke that cancelled it; that scan owns the results now.
            return;
        }
        catch (Exception ex)
        {
            // Typed input fails constantly while it is being typed; saying so in place beats a dialog storm.
            _loading = true;
            try
            {
                ShowResults([]);
                Error = ex.Message;
            }
            finally
            {
                _loading = false;
            }

            UpdateStatus();
            return;
        }

        if (token.IsCancellationRequested) return;

        Replace(found);
    }

    /// <summary>
    /// Puts a finished scan's notes on screen, re-applying what the user had unticked. The loading flag
    /// covers the whole repopulate rather than the scan: it is what stops filling the list from reading
    /// as the user changing the selection, and every write to Results happens in here.
    /// </summary>
    private void Replace(IReadOnlyList<NoteMatches> found)
    {
        _loading = true;
        try
        {
            Error = null;

            var rows = new List<NoteResultViewModel>(found.Count);
            foreach (var note in found)
            {
                var row = new NoteResultViewModel(note, OnNoteSelectionChanged);
                foreach (var match in row.Matches)
                    if (_excluded.Contains((note.FullPath, match.Offset)))
                        match.IsSelected = false;
                row.SyncFromMatches();
                rows.Add(row);
            }
            ShowResults(rows);
        }
        finally
        {
            _loading = false;
        }

        UpdateStatus();
    }

    private void CancelFind()
    {
        _findCts?.Cancel();
        _findCts?.Dispose();
        _findCts = null;
    }

    [RelayCommand]
    private void OpenNote(NoteResultViewModel? note)
    {
        if (note is null) return;
        Close();
        _openPath(note.FullPath);
    }

    [RelayCommand]
    private async Task ReplaceAll()
    {
        try
        {
            var selection = Selection();
            if (selection.Count == 0) return;

            var matches = selection.Sum(n => n.Matches.Count);
            if (!await _dialogs.ConfirmAsync(
                    $"Replace {Count(matches, "occurrence")} in {Count(selection.Count, "note")}? " +
                    "Only the most recent replace can be undone."))
                return;

            var report = _replace.Replace(selection);
            _undo = report.NotesChanged > 0 ? report.Snapshot : null;
            CanUndo = _undo is not null;
            _excluded.Clear();
            _applied(report);
            await RescanAsync();

            if (report.Skipped.Count > 0)
                await _dialogs.ShowErrorAsync(
                    "These notes changed on disk since the search and were left untouched:" +
                    Environment.NewLine + string.Join(Environment.NewLine, report.Skipped));
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task UndoReplace()
    {
        try
        {
            if (_undo is null) return;

            var snapshot = _undo;
            _replace.Restore(snapshot);
            ClearUndo();
            _applied(new ReplaceReport(snapshot.Count, 0, [], snapshot));
            await RescanAsync();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(ex.Message);
        }
    }

    /// <summary>The notes and matches the user left ticked, ready to hand to the service.</summary>
    public IReadOnlyList<NoteMatches> Selection() => Results
        .Select(note => new NoteMatches(
            note.FullPath,
            note.Name,
            note.Folder,
            note.Matches.Where(m => m.IsSelected).Select(m => m.Match).ToList()))
        .Where(note => note.Matches.Count > 0)
        .ToList();

    partial void OnFindChanged(string value) => Restart();
    partial void OnReplacementChanged(string value) => Restart();
    partial void OnMatchCaseChanged(bool value) => Restart();
    partial void OnWholeWordChanged(bool value) => Restart();
    partial void OnUseRegexChanged(bool value) => Restart();
    partial void OnScopeChanged(ScopeItem? value) => Restart();

    private void Restart()
    {
        if (_loading || !IsOpen) return;
        _rescanTimer.Stop();
        _rescanTimer.Start();
    }

    /// <summary>Puts rows on screen in one step and takes the running totals from them.</summary>
    private void ShowResults(IReadOnlyList<NoteResultViewModel> rows)
    {
        Results = rows;
        _selectedMatches = rows.Sum(n => n.SelectedCount);
        _selectedNotes = rows.Count(n => n.SelectedCount > 0);
    }

    /// <summary>
    /// One note's ticks changed, from <paramref name="previous"/> selected matches to what it has now.
    /// Only that note is resynced into the exclusions and the totals.
    /// </summary>
    private void OnNoteSelectionChanged(NoteResultViewModel note, int previous)
    {
        if (_loading) return;

        foreach (var match in note.Matches)
        {
            var key = (note.FullPath, match.Offset);
            if (match.IsSelected) _excluded.Remove(key);
            else _excluded.Add(key);
        }

        _selectedMatches += note.SelectedCount - previous;
        _selectedNotes += (note.SelectedCount > 0 ? 1 : 0) - (previous > 0 ? 1 : 0);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var matches = _selectedMatches;
        var notes = _selectedNotes;

        CanReplace = matches > 0;
        Status = !IsOpen || Find.Length == 0 || Error is not null
            ? string.Empty
            : matches == 0
                ? "No matches"
                : $"{Count(matches, "match", "matches")} in {Count(notes, "note")}";
    }

    private void RefreshFolders(string? folderScope)
    {
        Folders.Clear();
        Folders.Add(new ScopeItem("Whole vault", null));

        if (_vault.RootPath is not null)
            foreach (var entry in Flatten(_vault.GetTree(), string.Empty))
                Folders.Add(entry);

        Scope = Folders.FirstOrDefault(f => f.Path is not null
                    && string.Equals(f.Path, folderScope, StringComparison.OrdinalIgnoreCase))
                ?? Folders[0];
    }

    private static IEnumerable<ScopeItem> Flatten(IReadOnlyList<VaultEntry> entries, string prefix)
    {
        foreach (var entry in entries.Where(e => e.IsFolder))
        {
            var label = prefix.Length == 0 ? entry.Name : $"{prefix}/{entry.Name}";
            yield return new ScopeItem(label, entry.FullPath);
            foreach (var child in Flatten(entry.Children, label))
                yield return child;
        }
    }

    private static string Count(int n, string singular, string? plural = null) =>
        $"{n} {(n == 1 ? singular : plural ?? singular + "s")}";
}

/// <summary>One note in the results: its matches, and whether they are in or out (tri-state).</summary>
public sealed partial class NoteResultViewModel : ObservableObject
{
    private readonly Action<NoteResultViewModel, int> _changed;
    private bool _syncing;

    public string FullPath { get; }
    public string Name { get; }
    public string Folder { get; }

    /// <summary>Fixed once the note is found: a rescan builds new rows rather than editing these.</summary>
    public IReadOnlyList<MatchViewModel> Matches { get; }
    public int Count => Matches.Count;

    /// <summary>Matches still ticked, kept current so the overlay's totals never have to recount.</summary>
    public int SelectedCount { get; private set; }

    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
    [ObservableProperty] public partial bool? IsSelected { get; set; } = true;

    /// <param name="changed">Told after the user changes this note's ticks, with how many were ticked before.</param>
    public NoteResultViewModel(NoteMatches note, Action<NoteResultViewModel, int> changed)
    {
        _changed = changed;
        FullPath = note.FullPath;
        Name = note.Name;
        Folder = note.Folder;
        Matches = [.. note.Matches.Select(match => new MatchViewModel(match, OnMatchChanged))];
        SelectedCount = Matches.Count;
    }

    /// <summary>Recomputes the tri-state from the matches, after they were set without the user.</summary>
    public void SyncFromMatches()
    {
        _syncing = true;
        try
        {
            SelectedCount = Matches.Count(m => m.IsSelected);
            IsSelected = SelectedCount == 0 ? false : SelectedCount == Matches.Count ? true : null;
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnIsSelectedChanged(bool? value)
    {
        if (_syncing) return;

        // "Some" is something the matches say, never something set from outside; put it back.
        if (value is null)
        {
            SyncFromMatches();
            return;
        }

        var previous = SelectedCount;
        _syncing = true;
        try
        {
            foreach (var match in Matches)
                match.IsSelected = value.Value;
        }
        finally
        {
            _syncing = false;
        }
        SelectedCount = value.Value ? Matches.Count : 0;

        _changed(this, previous);
    }

    private void OnMatchChanged()
    {
        if (_syncing) return;
        var previous = SelectedCount;
        SyncFromMatches();
        _changed(this, previous);
    }
}

/// <summary>One occurrence: the line around it, split for display, and whether it is in the replace.</summary>
public sealed partial class MatchViewModel : ObservableObject
{
    private readonly Action _changed;

    public TextMatch Match { get; }
    public int Offset => Match.Offset;
    public int LineNumber => Match.LineNumber;
    public string Prefix => Match.Prefix;
    public string Found => Match.Found;
    public string Replacement => Match.Replacement;
    public string Suffix => Match.Suffix;

    [ObservableProperty] public partial bool IsSelected { get; set; } = true;

    public MatchViewModel(TextMatch match, Action changed)
    {
        Match = match;
        _changed = changed;
    }

    partial void OnIsSelectedChanged(bool value) => _changed();
}
