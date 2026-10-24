using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.Services.Markdown;

namespace MdNotes.ViewModels;

/// <summary>
/// The vault as the window works in it: the tree, what is selected, note and folder CRUD, wikilink
/// and drag-and-drop resolution, manual order, and the sidebar search. Split out of
/// <see cref="MainViewModel"/>, which now keeps only the window shell - view mode, theme, sidebar
/// visibility and the title. The two have different reasons to change: the shell needs the settings
/// and the dialogs, the workspace needs the vault, the search, the templates and the exclusions.
///
/// The editor is given rather than owned: the note belongs to the window, and the workspace has to
/// move it out of the way of a rename, a delete or a vault switch.
///
/// IDisposable because one vault feeds every window, so a closed window must let go of it.
/// </summary>
public sealed partial class VaultWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly VaultService _vault;
    private readonly ISearchService _search;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly INoteTemplateService _templates;
    private readonly EditorViewModel _editor;

    /// <summary>
    /// How much of a folder row's height, at each end, still means "put it beside me" rather than "put it
    /// inside me". A quarter leaves the middle half of the row as the into-target, which is wide enough to
    /// hit without aiming and narrow enough that reordering two folders stays possible.
    /// </summary>
    private const double IntoBandEdge = 0.25;

    private static readonly TimeSpan RefreshQuiet = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherTimer _refreshTimer = new() { Interval = RefreshQuiet };

    /// <summary>
    /// Watcher bursts, coalesced: one post to this thread per burst rather than one per event, and the
    /// tree refreshed once the burst has been quiet for the timer's interval - after it, as before.
    /// </summary>
    private readonly BurstGate _vaultBurst = new(RefreshQuiet);

    // 250 ms, the interval the other two keystroke-driven timers use (the editor preview and the
    // replace overlay's rescan). The 300 ms above is watcher-driven, which is a different question.
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly EventHandler _vaultChanged;
    private readonly EventHandler _vaultOpened;

    /// <summary>The scan in flight, cancelled by the next keystroke, an emptied box or a closed window.</summary>
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// Bumped by every refresh of the tree. A background walk captures it and drops its answer when it
    /// no longer matches - otherwise a slow walk finishing after a mutation would put the older vault
    /// back on screen.
    /// </summary>
    private int _treeGeneration;

    /// <summary>
    /// Every row by path, rebuilt each time the tree is reconciled - the only place rows come and go,
    /// besides the vault switch that clears both. Looking a path up used to walk the whole tree, and a
    /// drag did it twice per pointer move. Case-insensitive like the walk it replaces, and the first
    /// row in drawing order wins a collision, as the walk's FirstOrDefault did.
    /// </summary>
    private readonly Dictionary<string, TreeNodeViewModel> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The background tree walk in flight, or a completed task when there is none. The walk is started
    /// from an event - another window switching the vault reaches this one too - so there is no call
    /// chain to await it through, and this is what a test waits on instead of a delay.
    /// </summary>
    public Task TreeReady { get; private set; } = Task.CompletedTask;

    public QuickSwitcherViewModel QuickSwitcher { get; }
    public SearchReplaceViewModel SearchReplace { get; }
    public ObservableCollection<TreeNodeViewModel> Tree { get; } = [];
    /// <summary>Replaced wholesale per search, one notification rather than one per hit.</summary>
    [ObservableProperty] public partial IReadOnlyList<SearchHit> SearchResults { get; private set; } = [];

    [ObservableProperty] public partial TreeNodeViewModel? SelectedNode { get; set; }
    [ObservableProperty] public partial SearchHit? SelectedHit { get; set; }
    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSearching { get; private set; }

    /// <summary>
    /// The vault the window is showing. One vault serves every window, so these read the service
    /// rather than keeping a copy of it - a copy is what let one window rename the vault under
    /// another. <see cref="OnVaultOpened"/> is what tells the bindings to re-read.
    /// </summary>
    public string? VaultPath => _vault.RootPath;

    public string? VaultName => _vault.RootPath is { } root
        ? Path.GetFileName(root) is { Length: > 0 } name ? name : root
        : null;

    /// <summary>False until a vault is open - a window may show only a loose file.</summary>
    public bool HasVault => _vault.RootPath is not null;

    public VaultWorkspaceViewModel(AppServices services, EditorViewModel editor)
    {
        _vault = services.Vault;
        _search = services.Search;
        _settings = services.Settings;
        _dialogs = services.Dialogs;
        _templates = services.Templates;
        _editor = editor;

        QuickSwitcher = new QuickSwitcherViewModel(
            _vault,
            path => _dialogs.Guard(() => SelectPath(path)),
            name => _ = _dialogs.GuardAsync(async () =>
            {
                var path = CreateFromTemplate(RootPath, name);
                await RefreshTreeAfterEditAsync();
                SelectPath(path);
            }));
        SearchReplace = new SearchReplaceViewModel(
            services.Replace,
            _vault,
            _dialogs,
            path => _dialogs.Guard(() => SelectPath(path)),
            OnNotesRewritten);

        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            // Still mid-burst: wait out the rest of the quiet period rather than walk a vault that is changing.
            if (!_vaultBurst.TrySettle(out var remaining))
            {
                _refreshTimer.Interval = remaining;
                _refreshTimer.Start();
                return;
            }
            _ = _dialogs.GuardAsync(RefreshTreeAsync);
        };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); _ = _dialogs.GuardAsync(RunSearchAsync); };
        _vaultChanged = (_, _) =>
        {
            if (!_vaultBurst.Signal()) return;
            Dispatcher.UIThread.Post(() =>
            {
                _refreshTimer.Stop();
                _refreshTimer.Interval = RefreshQuiet;
                _refreshTimer.Start();
            });
        };
        _vault.VaultChanged += _vaultChanged;
        _vaultOpened = (_, _) => _ = _dialogs.GuardAsync(OnVaultOpenedAsync);
        _vault.VaultOpened += _vaultOpened;
        _editor.PropertyChanged += OnEditorPropertyChanged;
    }

    /// <summary>A file leaving or entering the vault is what decides whether it can be imported.</summary>
    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.IsLooseFile))
            ImportToVaultCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Detaches from the shared vault. One vault feeds every window, so a closed window must let go.</summary>
    public void Dispose()
    {
        _vault.VaultChanged -= _vaultChanged;
        _vault.VaultOpened -= _vaultOpened;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _refreshTimer.Stop();
        _searchTimer.Stop();

        // Both kinds of work in flight belong to a window that is going away: the scan is abandoned, and
        // the generation moves so a walk already running drops its answer rather than touching the tree.
        CancelSearch();
        _treeGeneration++;

        // The overlay is this workspace's, and it owns a timer and a find of its own.
        SearchReplace.Close();
    }

    /// <summary>Reopens the last vault when there is one. Never prompts, so a file can open without a vault.</summary>
    public bool TryRestoreVault()
    {
        var last = _settings.LastVaultPath;
        if (last is null || !Directory.Exists(last)) return false;
        OpenVaultAt(last);
        return true;
    }

    /// <summary>Opens a file that lives outside the vault, straight into the editor.</summary>
    public void OpenLooseFile(string path) => _dialogs.Guard(() =>
    {
        // The path comes off the command line, so it is checked here rather than trusted.
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}");
        SelectedNode = null;
        _editor.Load(Path.GetFullPath(path));
    });

    /// <summary>
    /// Opens the last vault, or asks for one. Returns false if the user cancelled.
    ///
    /// Waits for the first tree walk, because "the vault is open" is not true of a window still showing
    /// an empty sidebar. Waiting, not blocking: the walk is on the thread pool and the UI thread is free
    /// to paint throughout.
    /// </summary>
    public Task<bool> TryOpenInitialVaultAsync() => _dialogs.TryAsync(async () =>
    {
        if (TryRestoreVault())
        {
            await TreeReady;
            return true;
        }

        var picked = await _dialogs.PickFolderAsync();
        if (picked is null) return false;
        OpenVaultAt(picked);
        await TreeReady;
        return true;
    });

    [RelayCommand]
    private Task OpenVault() => _dialogs.GuardAsync(async () =>
    {
        var picked = await _dialogs.PickFolderAsync();
        if (picked is null) return;
        await _editor.FlushUnsavedAsync(_dialogs);
        _editor.SaveImplicitly();
        _editor.Discard();
        OpenVaultAt(picked);
        await TreeReady;
    });

    [RelayCommand(CanExecute = nameof(HasVault))]
    private Task NewNote() => _dialogs.GuardAsync(async () =>
    {
        var name = await _dialogs.PromptTextAsync("New note", "Untitled");
        if (name is null) return;
        var folder = TargetFolder();
        var path = CreateFromTemplate(folder, name);
        await RefreshTreeAfterEditAsync();
        SelectPath(path);
    });

    [RelayCommand(CanExecute = nameof(HasVault))]
    private Task NewFolder() => _dialogs.GuardAsync(async () =>
    {
        var name = await _dialogs.PromptTextAsync("New folder", "New folder");
        if (name is null) return;
        var path = _vault.CreateFolder(TargetFolder(), name);
        await RefreshTreeAfterEditAsync();
        SelectPath(path);
    });

    [RelayCommand]
    private Task Rename() => _dialogs.GuardAsync(async () =>
    {
        var node = SelectedNode;
        if (node is null) return;
        await RenameAsync(node.FullPath, node.Name);
    });

    [RelayCommand]
    private Task RenameCurrentNote() => _dialogs.GuardAsync(async () =>
    {
        if (_editor.CurrentPath is null) return;
        await RenameAsync(_editor.CurrentPath, _editor.NoteName ?? "");
    });

    private async Task RenameAsync(string path, string currentName)
    {
        var name = await _dialogs.PromptTextAsync("Rename", currentName);
        if (name is null || name == currentName) return;

        _editor.SaveImplicitly();
        var newPath = _vault.Rename(path, name);
        if (_editor.CurrentPath is { } current && PathScope.IsSameOrUnder(current, path))
            _editor.Retarget(newPath + current[path.Length..]);

        await RefreshTreeAfterEditAsync();
        SelectIfVisible(newPath);
    }

    [RelayCommand]
    private Task Delete() => _dialogs.GuardAsync(async () =>
    {
        var node = SelectedNode;
        if (node is null) return;
        var what = node.IsFolder ? "folder and everything in it" : "note";
        if (!await _dialogs.ConfirmAsync($"Delete the {what} '{node.Name}'?")) return;

        if (_editor.CurrentPath is { } current && PathScope.IsSameOrUnder(current, node.FullPath))
            _editor.Discard();
        _vault.Delete(node.FullPath);
        await RefreshTreeAfterEditAsync();
    });

    /// <summary>
    /// A wikilink click. A trailing '/' means the target is a folder; without one a note wins, and a
    /// folder of that name is tried before falling back to creating the note.
    /// </summary>
    [RelayCommand]
    private Task OpenWikiLink(string target) => _dialogs.GuardAsync(async () =>
    {
        var path = WikiLinkExtension.IsFolderTarget(target)
            ? ResolveFolderTarget(WikiLinkExtension.FolderName(target))
            : _vault.FindNoteByName(target)
              ?? _vault.FindFolderByName(target)
              ?? CreateFromTemplate(RootPath, target);

        await RefreshTreeAfterEditAsync();
        SelectPath(path);
        // SelectPath opens the ancestors; a folder the link named should also show what is in it.
        if (SelectedNode is { IsFolder: true } folder) folder.IsExpanded = true;
    });

    /// <summary>
    /// A missing folder is created at the root, as a missing note is - but only a plain name:
    /// conjuring a whole path from a typo is worse than saying it is not there.
    /// </summary>
    private string ResolveFolderTarget(string name) =>
        _vault.FindFolderByName(name)
        ?? (name.IndexOfAny(['/', '\\']) >= 0
            ? throw new DirectoryNotFoundException($"No folder at '{name}' in the vault.")
            : _vault.CreateFolder(RootPath, name));

    private bool CanImportToVault => HasVault && _editor.IsLooseFile;

    /// <summary>
    /// Copies the open loose file into the vault, current edits included. The original is left
    /// where it is - it is usually not ours to move.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImportToVault))]
    private Task ImportToVault() => _dialogs.GuardAsync(async () =>
    {
        if (_editor.CurrentPath is null) return;
        var name = await _dialogs.PromptTextAsync("Import into vault", _editor.NoteName ?? "Untitled");
        if (name is null) return;

        // The content already exists, so an import is never templated.
        var path = _vault.CreateNote(TargetFolder(), name, _editor.Text);
        await RefreshTreeAfterEditAsync();
        SelectPath(path);
    });

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in AllNodes())
            node.IsExpanded = false;
    }

    [RelayCommand(CanExecute = nameof(HasVault))]
    private void OpenQuickSwitcher() => _dialogs.Guard(QuickSwitcher.Open);

    [RelayCommand(CanExecute = nameof(HasVault))]
    private Task OpenSearchReplace() => _dialogs.GuardAsync(async () =>
    {
        // The overlay is modal, so flushing the buffer here is enough: nothing can dirty it again
        // before the replace, and the editor is reloaded afterwards from what was written. In manual
        // mode the replace acts on what is on disk, so the unsaved buffer is the user's call first.
        await _editor.FlushUnsavedAsync(_dialogs);
        _editor.SaveImplicitly();
        await SearchReplace.OpenAsync(SearchQuery, SelectedNode is { IsFolder: true } folder ? folder.FullPath : null);
    });

    /// <summary>Puts the tree and the open note back in step with what a replace or undo wrote.</summary>
    private void OnNotesRewritten(ReplaceReport report) => _ = _dialogs.GuardAsync(async () =>
    {
        await RefreshTreeAfterEditAsync();
        if (_editor.CurrentPath is { } current && report.Snapshot.ContainsKey(current))
            _editor.Load(current);
    });

    [RelayCommand(CanExecute = nameof(HasVault))]
    private Task MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasVault))]
    private Task MoveDown() => Move(1);

    [RelayCommand(CanExecute = nameof(HasVault))]
    private Task ResetOrder() => _dialogs.GuardAsync(async () =>
    {
        if (SelectedNode is not { } node) return;
        _vault.ResetOrder(ParentFolder(node));
        await RefreshTreeAfterEditAsync();
    });

    /// <summary>Executes a drop resolved by <see cref="ResolveDrop"/>.</summary>
    public Task MoveNode(TreeNodeViewModel node, string targetFolder, int index) =>
        _dialogs.GuardAsync(() => MoveToAsync(node, targetFolder, index));

    /// <summary>
    /// What a pointer <paramref name="fraction"/> of the way down the hovered row asks for. A folder takes
    /// a drop into it across its middle band, and every row otherwise splits at its midpoint into before
    /// and after. This is a product rule, not a geometry detail: the sidebar measures the fraction and this
    /// says what the user meant by it, so it can be exercised without a pointer-driven UI test.
    /// </summary>
    public static DropPosition PositionIn(TreeNodeViewModel over, double fraction) =>
        over.IsFolder && fraction is > IntoBandEdge and < 1 - IntoBandEdge ? DropPosition.Into
        : fraction < 0.5 ? DropPosition.Before
        : DropPosition.After;

    /// <summary>
    /// Where a drag of <paramref name="node"/> onto <paramref name="over"/> would land, as a folder and an
    /// index counted with the node removed. Null when the drop is not allowed or would change nothing.
    /// </summary>
    public (string Folder, int Index)? ResolveDrop(TreeNodeViewModel node, TreeNodeViewModel? over, DropPosition where)
    {
        if (over is not null && PathScope.IsSameOrUnder(over.FullPath, node.FullPath)) return null;

        var (folder, siblings, insertAt) = over switch
        {
            null => (RootPath, (IList<TreeNodeViewModel>)Tree, Tree.Count),
            { IsFolder: true } target when where is DropPosition.Into =>
                (target.FullPath, (IList<TreeNodeViewModel>)target.Children, target.Children.Count),
            _ => Between(over, where),
        };

        var current = siblings.IndexOf(node);
        var index = current >= 0 && current < insertAt ? insertAt - 1 : insertAt;
        return current == index ? null : (folder, index);
    }

    private (string, IList<TreeNodeViewModel>, int) Between(TreeNodeViewModel over, DropPosition where)
    {
        var siblings = SiblingsOf(over);
        return (ParentFolder(over), siblings, siblings.IndexOf(over) + (where is DropPosition.After ? 1 : 0));
    }

    private Task Move(int delta) => _dialogs.GuardAsync(async () =>
    {
        if (SelectedNode is not { } node) return;
        var siblings = SiblingsOf(node);
        var index = siblings.IndexOf(node) + delta;
        if (index < 0 || index >= siblings.Count) return;
        await MoveToAsync(node, ParentFolder(node), index);
    });

    private async Task MoveToAsync(TreeNodeViewModel node, string targetFolder, int index)
    {
        _editor.SaveImplicitly();
        var newPath = _vault.MoveTo(node.FullPath, targetFolder, index);
        if (_editor.CurrentPath is { } current && PathScope.IsSameOrUnder(current, node.FullPath))
            _editor.Retarget(newPath + current[node.FullPath.Length..]);

        await RefreshTreeAfterEditAsync();
        SelectPath(newPath);
    }

    /// <summary>
    /// Selecting a note is what opens it. The TreeView's SelectedItem binding is the one owner of
    /// what is selected - the row itself holds no flag of its own to keep in step with this.
    /// </summary>
    partial void OnSelectedNodeChanged(TreeNodeViewModel? value)
    {
        if (value is null) return;
        if (value.IsFolder || value.FullPath == _editor.CurrentPath) return;
        _ = _dialogs.GuardAsync(async () =>
        {
            await _editor.FlushUnsavedAsync(_dialogs);
            _editor.Load(value.FullPath);
        });
    }

    partial void OnSelectedHitChanged(SearchHit? value)
    {
        if (value is not null) _dialogs.Guard(() => SelectPath(value.FullPath));
    }

    /// <summary>
    /// The pane swaps on the keystroke - that is the user's own doing - but the vault is only read
    /// once the typing stops: a search reads every note in the vault, and five characters used to
    /// mean five whole-vault reads. The previous hits stay up for that one beat rather than blinking
    /// the list to empty, which would read as "no matches" mid-word.
    ///
    /// Emptying the box is never debounced, and stops a pending scan: the clear button and a vault
    /// switch both arrive here, and a timer left armed would search the new vault for the old
    /// vault's query.
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        _searchTimer.Stop();

        // Whatever is still scanning is scanning for the query as it read before this keystroke.
        CancelSearch();
        IsSearching = !string.IsNullOrWhiteSpace(value);

        if (!IsSearching)
        {
            SelectedHit = null;
            SearchResults = [];
            return;
        }

        _searchTimer.Start();
    }

    /// <summary>
    /// The scan itself, split off the debounce so the timer tick reads as one line. It reads every note
    /// in the vault, so it runs on the thread pool rather than here: measured at 130 ms over two
    /// thousand notes and 320 ms over five thousand, which on the UI thread is that long with the window
    /// frozen, for every word typed. The debounce decides how often that is paid, never how long it lasts.
    /// </summary>
    private async Task RunSearchAsync()
    {
        CancelSearch();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var query = SearchQuery;

        IReadOnlyList<SearchHit> hits;
        try
        {
            hits = await Task.Run(() => _search.Search(query, token), token);
        }
        catch (OperationCanceledException)
        {
            // A newer query, an emptied box, or a closed window. What is on screen belongs to whichever
            // query is the current one now, so it is left exactly as it is.
            return;
        }

        // Cancelled between the last note and here: the task ran to completion so nothing threw, but
        // these hits are still the previous query's.
        if (token.IsCancellationRequested) return;

        // Searched before cleared: a note deleted mid-scan then leaves the previous hits standing and
        // goes through the guard, rather than leaving an empty pane behind a dialog.
        SelectedHit = null;
        SearchResults = hits;
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private string RootPath => _vault.RootPath ?? throw new InvalidOperationException("No vault is open.");

    private void OpenVaultAt(string path)
    {
        // A window joining the vault another window already opened gets no event, but its own tree is
        // still empty - so it syncs itself. A real switch is announced to every window, this one included.
        if (!_vault.Open(path)) _ = _dialogs.GuardAsync(OnVaultOpenedAsync);
        _settings.LastVaultPath = _vault.RootPath;
    }

    /// <summary>
    /// The vault was switched - here, or in another window. Everything this window shows of the old
    /// vault is now wrong, so the title, the tree, the search and the one-step undo all reset. The
    /// open note deliberately survives: it belongs to whoever was editing it, and silently closing
    /// another window's buffer would lose their work. It simply becomes a loose file.
    /// </summary>
    private async Task OnVaultOpenedAsync()
    {
        OnPropertyChanged(nameof(VaultPath));
        OnPropertyChanged(nameof(VaultName));
        OnPropertyChanged(nameof(HasVault));
        NewNoteCommand.NotifyCanExecuteChanged();
        NewFolderCommand.NotifyCanExecuteChanged();
        OpenQuickSwitcherCommand.NotifyCanExecuteChanged();
        OpenSearchReplaceCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        ResetOrderCommand.NotifyCanExecuteChanged();
        ImportToVaultCommand.NotifyCanExecuteChanged();

        SearchQuery = string.Empty;
        SearchReplace.Close();
        SearchReplace.ClearUndo();
        SelectedNode = null;

        // A different vault shares no path with this tree, so it is dropped rather than diffed away
        // row by row - one Reset notification instead of one Remove per row.
        Tree.Clear();
        _byPath.Clear();
        _editor.NotifyVaultChanged();

        // The one walk that is always cold: nothing has been read yet, and with note titles on it opens
        // every note in the vault for its head. Everything above this line has already taken effect.
        await RefreshTreeAsync();
    }

    /// <summary>
    /// Puts the tree back in step with the disk after this window changed the vault itself. Awaited,
    /// because these callers need the row they made before they can select it.
    ///
    /// Here and now while the walk is cheap, and then the returned task is already complete: one entry
    /// changed, and either no note is opened for its title or the content cache still holds every other
    /// note's head. Past the cache's budget neither is true - the walk would re-read a large share of
    /// the vault on the UI thread, the freeze <see cref="RefreshTreeAsync"/> exists to avoid - so it goes
    /// there instead, and the caller's selection waits for it. The same holds while a background walk is
    /// still running: the cache has not read the vault yet, so it holds nothing to make this one cheap.
    /// </summary>
    private Task RefreshTreeAfterEditAsync()
    {
        if (!TreeReady.IsCompleted || !_vault.TreeWalkIsCheap) return RefreshTreeAsync();

        // Moving the generation is what tells a background walk already running that its answer is
        // older than the tree about to be on screen.
        _treeGeneration++;
        Reconcile(_vault.GetTree());
        return Task.CompletedTask;
    }

    /// <summary>
    /// The same refresh off the UI thread, for the two places the walk is cold and unbounded: opening a
    /// vault, and the watcher saying the vault changed under us. With note titles on the walk reads the
    /// head of every note - measured at 244 ms over two thousand notes and 559 ms over five thousand,
    /// every millisecond of it a frozen window if it runs here.
    /// </summary>
    private Task RefreshTreeAsync() => TreeReady = WalkAsync();

    private async Task WalkAsync()
    {
        var generation = ++_treeGeneration;
        var tree = await Task.Run(_vault.GetTree);

        // The tree was refreshed while this was walking, so this answer is the older of the two.
        if (generation != _treeGeneration) return;

        Reconcile(tree);
    }

    /// <summary>
    /// The rows are reconciled rather than replaced, so a row that is still in the vault is the same
    /// object afterwards - this preserves no view state because none of it is lost, and a new piece of
    /// it costs nothing here.
    /// </summary>
    private void Reconcile(IReadOnlyList<VaultEntry> tree)
    {
        // Read before the tree moves, not after: removing the selected row makes the TreeView write
        // null back through the two-way SelectedItem binding, and the path is what survives that.
        var selected = SelectedNode?.FullPath;

        TreeNodeViewModel.Reconcile(Tree, tree);
        _byPath.Clear();
        foreach (var node in AllNodes()) _byPath.TryAdd(node.FullPath, node);

        // Keep the selected item, including one just moved; fall back to the open note once it is gone.
        // A row that survived is found again by reference, so this assignment is a no-op.
        SelectedNode = (selected is null ? null : FindNode(selected))
                       ?? (_editor.CurrentPath is { } open ? FindNode(open) : null);
    }

    /// <summary>
    /// Selects a path when the sidebar can show it. A note that the exclusion list hides stays open in
    /// the editor without a row of its own, so renaming it must not be an error.
    /// </summary>
    private void SelectIfVisible(string path)
    {
        if (FindNode(path) is not null) SelectPath(path);
    }

    private void SelectPath(string path)
    {
        var node = FindNode(path) ?? throw new FileNotFoundException($"Not found in vault tree: {path}");
        // Its ancestors, by walking up rather than testing every folder in the vault against the path.
        for (var folder = node.Parent; folder is not null; folder = folder.Parent)
            folder.IsExpanded = true;
        SelectedNode = node;
    }

    /// <summary>
    /// A note born from a name the user gave starts from the template. An import does not: it already
    /// has its content. This is the only place a template is applied.
    /// </summary>
    private string CreateFromTemplate(string folder, string name) =>
        _vault.CreateNote(folder, name, _templates.ContentFor(folder, name));

    private string TargetFolder() => SelectedNode switch
    {
        { IsFolder: true } folder => folder.FullPath,
        { } note => Path.GetDirectoryName(note.FullPath) ?? RootPath,
        null => RootPath,
    };

    private IEnumerable<TreeNodeViewModel> AllNodes() => TreeNodeViewModel.Walk(Tree);

    public TreeNodeViewModel? FindNode(string path) => _byPath.GetValueOrDefault(path);

    private string ParentFolder(TreeNodeViewModel node) => node.Parent?.FullPath ?? RootPath;

    private IList<TreeNodeViewModel> SiblingsOf(TreeNodeViewModel node) =>
        node.Parent?.Children ?? (IList<TreeNodeViewModel>)Tree;
}
