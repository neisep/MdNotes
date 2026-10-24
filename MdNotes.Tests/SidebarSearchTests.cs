using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// The sidebar search box, over a real vault. The box updated per keystroke and every search reads
/// every note in the vault, so five characters meant five whole-vault reads; the scan now waits for
/// the typing to stop. AvaloniaFact throughout: MainViewModel owns DispatcherTimers.
/// </summary>
public sealed class SidebarSearchTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly FakeDialogs _dialogs = new();

    public SidebarSearchTests()
    {
        _vault.Note("Alpha.md", "the alpha widget");
        _vault.Note("Beta.md", "the beta widget");
        _vault.Note("Gamma.md", "nothing relevant");
    }

    public void Dispose() => _vault.Dispose();

    private async Task<MainViewModel> OpenAsync(ISearchService? search = null)
    {
        _vault.Settings.LastVaultPath = _vault.Root;
        var services = TestServices.For(_vault, _dialogs);
        var vm = new MainViewModel(search is null ? services : services with { Search = search });

        Assert.True(await vm.TryOpenInitialVaultAsync());
        Assert.Empty(_dialogs.Errors);
        return vm;
    }

    /// <summary>The scan is debounced, so it has to be waited out.</summary>
    private static async Task SettleAsync()
    {
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Finding 13 for the sidebar: the hits are one assignment, not one Add per hit.</summary>
    [AvaloniaFact]
    public async Task A_search_lands_its_hits_in_one_notification()
    {
        using var vm = await OpenAsync();
        var notifications = 0;
        vm.Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Workspace.SearchResults)) notifications++;
        };

        vm.Workspace.SearchQuery = "widget";
        await SettleAsync();

        Assert.Equal(1, notifications);
        Assert.Equal(2, vm.Workspace.SearchResults.Count);
    }

    [AvaloniaFact]
    public async Task The_results_land_after_the_debounce()
    {
        using var vm = await OpenAsync();

        vm.Workspace.SearchQuery = "widget";
        Assert.Empty(vm.Workspace.SearchResults);

        await SettleAsync();

        Assert.Equal(["Alpha", "Beta"], vm.Workspace.SearchResults.Select(h => h.Name).Order());
        Assert.Empty(_dialogs.Errors);
    }

    /// <summary>
    /// The finding this whole change exists for: the box is bound with the default per-keystroke
    /// trigger, and a search reads the content of every note in the vault.
    /// </summary>
    [AvaloniaFact]
    public async Task Typing_five_characters_reads_the_vault_once()
    {
        var counting = new CountingSearch(new SearchService(_vault.Service));
        using var vm = await OpenAsync(counting);

        foreach (var query in new[] { "w", "wi", "wid", "widg", "widge" })
            vm.Workspace.SearchQuery = query;

        Assert.Equal(0, counting.Searches);

        await SettleAsync();

        Assert.Equal(1, counting.Searches);
        Assert.Equal(["Alpha", "Beta"], vm.Workspace.SearchResults.Select(h => h.Name).Order());
    }

    /// <summary>
    /// Picking a hit is what the results list is for, and it is the one path out of the search pane:
    /// it reveals the note in the tree and opens it. Nothing covered it before.
    /// </summary>
    [AvaloniaFact]
    public async Task Picking_a_hit_reveals_and_opens_that_note()
    {
        using var vm = await OpenAsync();
        vm.Workspace.SearchQuery = "alpha";
        await SettleAsync();

        vm.Workspace.SelectedHit = vm.Workspace.SearchResults.Single(h => h.Name == "Alpha");

        Assert.Equal("Alpha", vm.Workspace.SelectedNode?.Name);
        Assert.Equal(Path.Combine(_vault.Root, "Alpha.md"), vm.Editor.CurrentPath);
        Assert.Empty(_dialogs.Errors);
    }

    /// <summary>
    /// The clear button and a vault switch both arrive as an empty query and must take effect at
    /// once - waiting 250 ms to put the tree back would feel like the button had not worked.
    /// </summary>
    [AvaloniaFact]
    public async Task Clearing_the_box_puts_the_tree_back_at_once()
    {
        using var vm = await OpenAsync();
        vm.Workspace.SearchQuery = "widget";
        await SettleAsync();
        Assert.NotEmpty(vm.Workspace.SearchResults);

        vm.Workspace.SearchQuery = string.Empty;

        Assert.False(vm.Workspace.IsSearching);
        Assert.Empty(vm.Workspace.SearchResults);
        Assert.Null(vm.Workspace.SelectedHit);
    }

    /// <summary>
    /// Blinking the list to empty on every keystroke reads as "no matches", which is the one thing it
    /// must not say while the word is still being typed.
    /// </summary>
    [AvaloniaFact]
    public async Task The_previous_hits_stay_up_while_the_next_query_is_being_typed()
    {
        using var vm = await OpenAsync();
        vm.Workspace.SearchQuery = "widget";
        await SettleAsync();

        vm.Workspace.SearchQuery = "widgets-that-do-not-exist";

        Assert.Equal(["Alpha", "Beta"], vm.Workspace.SearchResults.Select(h => h.Name).Order());

        await SettleAsync();

        Assert.Empty(vm.Workspace.SearchResults);
    }

    /// <summary>The pane swap is feedback for the user's own keypress, so it is never debounced.</summary>
    [AvaloniaFact]
    public async Task The_pane_swaps_to_the_results_list_on_the_first_keystroke()
    {
        using var vm = await OpenAsync();
        Assert.False(vm.Workspace.IsSearching);

        vm.Workspace.SearchQuery = "w";

        Assert.True(vm.Workspace.IsSearching);
    }

    /// <summary>A pending scan must not outlive the vault it was asked about.</summary>
    [AvaloniaFact]
    public async Task Switching_the_vault_disarms_a_pending_search()
    {
        using var vm = await OpenAsync();
        var other = Directory.CreateTempSubdirectory("mdnotes-other-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(other, "Delta.md"), "the delta widget");
            vm.Workspace.SearchQuery = "widget";

            _dialogs.PickFolderResult = other;
            await vm.Workspace.OpenVaultCommand.ExecuteAsync(null);
            await SettleAsync();

            Assert.Equal(string.Empty, vm.Workspace.SearchQuery);
            Assert.Empty(vm.Workspace.SearchResults);
            Assert.False(vm.Workspace.IsSearching);
            Assert.Empty(_dialogs.Errors);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    /// <summary>
    /// The scan runs on the thread pool now, so the keystroke that supersedes it has to reach it
    /// somehow. Blocking inside the scan is what makes that observable: the test can hold one there,
    /// type the next character, and look at the token the scan was handed.
    /// </summary>
    [AvaloniaFact]
    public async Task The_next_keystroke_cancels_the_scan_already_running()
    {
        var search = new BlockingSearch();
        using var vm = await OpenAsync(search);

        vm.Workspace.SearchQuery = "wid";
        await SettleAsync();
        await search.Started;

        Assert.False(search.Token.IsCancellationRequested);

        vm.Workspace.SearchQuery = "widget";

        Assert.True(search.Token.IsCancellationRequested);

        // Emptying the box stops the timer, so releasing the held scan cannot start another one.
        vm.Workspace.SearchQuery = string.Empty;
        search.Release();
    }

    /// <summary>Holds a scan open until the test lets go, and keeps the token it was given.</summary>
    private sealed class BlockingSearch : ISearchService
    {
        private readonly SemaphoreSlim _started = new(0);
        private readonly SemaphoreSlim _release = new(0);

        public CancellationToken Token { get; private set; }

        public Task Started => _started.WaitAsync();

        public void Release() => _release.Release();

        public IReadOnlyList<SearchHit> Search(string query, CancellationToken token = default)
        {
            Token = token;
            _started.Release();
            _release.Wait(CancellationToken.None);
            token.ThrowIfCancellationRequested();
            return [];
        }
    }

    /// <summary>
    /// Counts what reaches the real service. Not a fake - the vault and the SearchService underneath
    /// are the real ones; this only watches how often they are asked.
    /// </summary>
    private sealed class CountingSearch(ISearchService inner) : ISearchService
    {
        public int Searches { get; private set; }

        /// <summary>The token of the most recent scan, so a test can assert the previous one was cancelled.</summary>
        public CancellationToken LastToken { get; private set; }

        public IReadOnlyList<SearchHit> Search(string query, CancellationToken token = default)
        {
            Searches++;
            LastToken = token;
            return inner.Search(query, token);
        }
    }
}
