using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.ViewModels;

public sealed partial class QuickSwitcherViewModel : ObservableObject
{
    private readonly VaultService _vault;
    private readonly Action<string> _openPath;
    private readonly Action<string> _createNote;

    /// <summary>The most rows the switcher lists.</summary>
    private const int Limit = 50;

    /// <summary>
    /// The vault's notes as switcher rows, built from one tree and kept until <see cref="VaultService.GetTree"/>
    /// hands back a different one - which it does only when the vault changes. Re-flattening per keystroke
    /// allocated a row and folder strings per note for a list that had not changed.
    /// </summary>
    private IReadOnlyList<VaultEntry>? _flattenedFrom;
    private Candidate[] _candidates = [];
    private List<SwitcherItem> _firstByName = [];

    /// <summary>A row with its names folded once, so ranking never folds a candidate again.</summary>
    private readonly record struct Candidate(SwitcherItem Item, string FoldedName, string FoldedFileName);

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Query { get; set; } = string.Empty;
    [ObservableProperty] public partial SwitcherItem? Selected { get; set; }
    public ObservableCollection<SwitcherItem> Results { get; } = [];

    /// <summary>
    /// A row of the switcher. <paramref name="Name"/> is what is shown - the declared title when there
    /// is one - and <paramref name="FileName"/> is what the note is called on disk. Both are matched
    /// against, so turning titles on never makes a note harder to reach than it was.
    /// </summary>
    public sealed record SwitcherItem(string Name, string Folder, string? FullPath, bool IsCreate, string FileName = "");

    public QuickSwitcherViewModel(VaultService vault, Action<string> openPath, Action<string> createNote)
    {
        _vault = vault;
        _openPath = openPath;
        _createNote = createNote;
    }

    [RelayCommand]
    public void Open()
    {
        if (Query.Length == 0)
            RefreshResults();
        else
            Query = string.Empty;
        IsOpen = true;
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;

        var currentIndex = Selected is null ? -1 : Results.IndexOf(Selected);
        var newIndex = Math.Clamp(currentIndex + delta, 0, Results.Count - 1);
        Selected = Results[newIndex];
    }

    [RelayCommand]
    public void OpenSelected()
    {
        if (Selected is not null)
        {
            if (Selected.IsCreate)
            {
                _createNote(Query.Trim());
            }
            else
            {
                _openPath(Selected.FullPath!);
            }
        }

        Close();
    }

    partial void OnQueryChanged(string value)
    {
        RefreshResults();
    }

    /// <summary>Rebuilds <see cref="Results"/> for the current <see cref="Query"/>. Internal rather
    /// than private only so MdNotes.Benchmarks can time one keystroke without triggering two.</summary>
    internal void RefreshResults()
    {
        Results.Clear();
        Flatten(_vault.GetTree());

        // If query is empty, list all notes sorted by name
        if (string.IsNullOrWhiteSpace(Query))
        {
            foreach (var note in _firstByName)
                Results.Add(note);
        }
        else
        {
            foreach (var note in Ranked(Query.Trim()))
                Results.Add(note);

            // If no matches and query is non-empty, add a create item
            if (Results.Count == 0)
            {
                Results.Add(new SwitcherItem(Query.Trim(), "", null, IsCreate: true));
            }
        }

        Selected = Results.FirstOrDefault();
    }

    /// <summary>
    /// The best <see cref="Limit"/> rows for <paramref name="query"/>, best first: by rank, then by name,
    /// then in tree order - what the LINQ chain this replaced produced, since its sort was stable. The
    /// same rule wikilinks resolve by, so "jonkoping" reaches "Jönköping" and an exact name still beats
    /// one that only matches once its accents are folded away; a row is ranked by the better of its
    /// shown name and its file name.
    ///
    /// Kept in a heap bounded at the limit, worst on top, rather than sorting every match to take 50: a
    /// broad query matches most of the vault, and only the rows that will be shown need ordering.
    /// </summary>
    private List<SwitcherItem> Ranked(string query)
    {
        var foldedQuery = DiacriticFolder.Fold(query);
        var kept = new PriorityQueue<int, RankKey>(Comparer<RankKey>.Create((a, b) => Compare(b, a)));

        for (var i = 0; i < _candidates.Length; i++)
        {
            var (item, foldedName, foldedFileName) = _candidates[i];
            var rank = Best(
                NameMatch.Rank(item.Name, foldedName, query, foldedQuery),
                NameMatch.Rank(item.FileName, foldedFileName, query, foldedQuery));
            if (rank == NameRank.None) continue;

            var key = new RankKey(rank, item.Name, i);
            if (kept.Count < Limit) kept.Enqueue(i, key);
            else if (kept.TryPeek(out _, out var worst) && Compare(key, worst) < 0) kept.EnqueueDequeue(i, key);
        }

        var best = new List<(RankKey Key, SwitcherItem Item)>(kept.Count);
        while (kept.TryDequeue(out var index, out var key)) best.Add((key, _candidates[index].Item));
        best.Sort((a, b) => Compare(a.Key, b.Key));
        return best.ConvertAll(x => x.Item);
    }

    private readonly record struct RankKey(NameRank Rank, string Name, int Index);

    /// <summary>Rank, then name as the old ThenBy compared it (the culture-aware default), then tree order.</summary>
    private static int Compare(RankKey a, RankKey b)
    {
        // As ints: Enum.CompareTo takes object, and boxing here ran once per heap comparison.
        var byRank = ((int)a.Rank).CompareTo((int)b.Rank);
        if (byRank != 0) return byRank;
        var byName = Comparer<string>.Default.Compare(a.Name, b.Name);
        return byName != 0 ? byName : a.Index.CompareTo(b.Index);
    }

    /// <summary>Rebuilds the candidates when, and only when, the vault hands back a different tree.</summary>
    private void Flatten(IReadOnlyList<VaultEntry> tree)
    {
        if (ReferenceEquals(tree, _flattenedFrom)) return;

        var notes = new List<SwitcherItem>();
        FlattenNotes(tree, "", notes);
        _candidates = [.. notes.Select(n => new Candidate(n, DiacriticFolder.Fold(n.Name), DiacriticFolder.Fold(n.FileName)))];
        _firstByName = [.. notes.OrderBy(n => n.Name).Take(Limit)];
        _flattenedFrom = tree;
    }

    /// <summary>The better of two ranks. NameRank is ordered best-first, so that is the smaller one.</summary>
    private static NameRank Best(NameRank a, NameRank b) => a <= b ? a : b;

    private static void FlattenNotes(IReadOnlyList<VaultEntry> entries, string folder, List<SwitcherItem> notes)
    {
        foreach (var entry in entries)
        {
            if (entry.IsFolder)
            {
                var subFolder = string.IsNullOrEmpty(folder) ? entry.Name : $"{folder}/{entry.Name}";
                FlattenNotes(entry.Children, subFolder, notes);
            }
            else
            {
                notes.Add(new SwitcherItem(entry.Display, folder, entry.FullPath, IsCreate: false, entry.Name));
            }
        }
    }
}
