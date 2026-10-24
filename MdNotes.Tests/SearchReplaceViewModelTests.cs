using Avalonia.Headless.XUnit;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// AvaloniaFact throughout: the view model owns a DispatcherTimer, so it needs a UI thread. Tests drive
/// the scan with RescanAsync() and await it, rather than waiting out the debounce - the find runs on the
/// thread pool now, so "it has happened" is the task completing rather than the call returning.
/// </summary>
public sealed class SearchReplaceViewModelTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly List<ReplaceReport> _applied = [];
    private readonly List<string> _opened = [];

    public SearchReplaceViewModelTests()
    {
        _vault.Note("Alpha.md", "the widget is a widget");
        _vault.Note("sub/Beta.md", "one widget here");
        _vault.Note("sub/Gamma.md", "nothing to see");
    }

    /// <summary>The overlay owns a timer and, now, a find on the thread pool; Close stops both.</summary>
    private SearchReplaceViewModel? _open;

    public void Dispose()
    {
        _open?.Close();
        _vault.Dispose();
    }

    private async Task<SearchReplaceViewModel> OpenAsync(string find = "widget", string replacement = "gadget")
    {
        var vm = new SearchReplaceViewModel(
            new ReplaceService(_vault.Service),
            _vault.Service,
            _dialogs,
            _opened.Add,
            _applied.Add);

        _open = vm;
        await vm.OpenAsync(find, null);
        vm.Replacement = replacement;
        await vm.RescanAsync();
        return vm;
    }

    [AvaloniaFact]
    public async Task Open_lists_every_match_grouped_by_note()
    {
        var vm = await OpenAsync();

        Assert.True(vm.IsOpen);
        Assert.Equal(["Alpha", "Beta"], vm.Results.Select(r => r.Name).Order());
        Assert.Equal("3 matches in 2 notes", vm.Status);
        Assert.True(vm.CanReplace);
    }

    [AvaloniaFact]
    public async Task Each_match_previews_its_replacement()
    {
        var match = (await OpenAsync()).Results.Single(r => r.Name == "Beta").Matches.Single();

        Assert.Equal("widget", match.Found);
        Assert.Equal("gadget", match.Replacement);
        Assert.Equal(1, match.LineNumber);
    }

    [AvaloniaFact]
    public async Task The_folder_scope_list_leaves_out_excluded_folders()
    {
        _vault.Folder("node_modules");
        _vault.Ignore("node_modules");

        var vm = await OpenAsync();

        Assert.DoesNotContain(vm.Folders, f => f.Label == "node_modules");
        Assert.Contains(vm.Folders, f => f.Label == "sub");
    }

    [AvaloniaFact]
    public async Task Folder_scope_narrows_the_results()
    {
        var vm = await OpenAsync();

        vm.Scope = vm.Folders.Single(f => f.Label == "sub");
        await vm.RescanAsync();

        Assert.Equal("Beta", Assert.Single(vm.Results).Name);
    }

    [AvaloniaFact]
    public async Task Unticking_a_note_unticks_its_matches_and_drops_it_from_the_selection()
    {
        var vm = await OpenAsync();
        var alpha = vm.Results.Single(r => r.Name == "Alpha");

        alpha.IsSelected = false;

        Assert.All(alpha.Matches, m => Assert.False(m.IsSelected));
        Assert.Equal("Beta", Assert.Single(vm.Selection()).Name);
        Assert.Equal("1 match in 1 note", vm.Status);
    }

    [AvaloniaFact]
    public async Task Unticking_one_match_leaves_its_note_half_selected()
    {
        var vm = await OpenAsync();
        var alpha = vm.Results.Single(r => r.Name == "Alpha");

        alpha.Matches[0].IsSelected = false;

        Assert.Null(alpha.IsSelected);
        Assert.Single(vm.Selection().Single(n => n.Name == "Alpha").Matches);
    }

    /// <summary>
    /// Finding 13: a scan's rows arrive in one step - one change notification for the list, where filling
    /// a bound collection row by row raised one per note.
    /// </summary>
    [AvaloniaFact]
    public async Task A_rescan_replaces_the_results_in_one_notification()
    {
        var vm = await OpenAsync();
        var notifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Results)) notifications++; };

        vm.Replacement = "doohickey";
        await vm.RescanAsync();

        Assert.Equal(1, notifications);
        Assert.Equal(2, vm.Results.Count);
    }

    /// <summary>
    /// Finding 20: the status totals are kept as each click happens rather than recounted, so they have
    /// to agree with a recount after any mix of match-level and note-level clicks.
    /// </summary>
    [AvaloniaFact]
    public async Task Running_totals_agree_with_a_recount_through_match_and_note_clicks()
    {
        var vm = await OpenAsync();
        var alpha = vm.Results.Single(r => r.Name == "Alpha");
        var beta = vm.Results.Single(r => r.Name == "Beta");

        alpha.Matches[0].IsSelected = false;
        Assert.Equal("2 matches in 2 notes", vm.Status);

        beta.IsSelected = false;
        Assert.Equal("1 match in 1 note", vm.Status);

        alpha.Matches[1].IsSelected = false;
        Assert.Equal("No matches", vm.Status);
        Assert.False(vm.CanReplace);

        alpha.IsSelected = true;
        beta.Matches[0].IsSelected = true;
        Assert.Equal("3 matches in 2 notes", vm.Status);
        Assert.Equal(3, vm.Selection().Sum(n => n.Matches.Count));
    }

    /// <summary>
    /// Finding 19: unticked matches are kept across rescans, not across sessions. Closing the overlay
    /// forgets them, so reopening it on the same search starts with everything ticked.
    /// </summary>
    [AvaloniaFact]
    public async Task Closing_the_overlay_forgets_what_was_unticked()
    {
        var vm = await OpenAsync();
        vm.Results.Single(r => r.Name == "Alpha").Matches[0].IsSelected = false;

        vm.Close();
        await vm.OpenAsync("widget", null);

        Assert.All(vm.Results.SelectMany(r => r.Matches), m => Assert.True(m.IsSelected));
    }

    [AvaloniaFact]
    public async Task Excluded_matches_survive_a_change_to_the_replacement_text()
    {
        var vm = await OpenAsync();
        vm.Results.Single(r => r.Name == "Alpha").Matches[0].IsSelected = false;

        vm.Replacement = "doohickey";
        await vm.RescanAsync();

        var alpha = vm.Results.Single(r => r.Name == "Alpha");
        Assert.False(alpha.Matches[0].IsSelected);
        Assert.True(alpha.Matches[1].IsSelected);
        Assert.Equal("doohickey", alpha.Matches[1].Replacement);
    }

    [AvaloniaFact]
    public async Task Invalid_regex_is_reported_in_place_and_clears_the_results()
    {
        var vm = await OpenAsync();

        vm.UseRegex = true;
        vm.Find = "[";
        await vm.RescanAsync();

        Assert.NotNull(vm.Error);
        Assert.Empty(vm.Results);
        Assert.Empty(vm.Status);
        Assert.False(vm.CanReplace);
        Assert.Empty(_dialogs.Errors); // typed input never raises a dialog
    }

    [AvaloniaFact]
    public async Task Replace_all_writes_the_selection_and_reports_it()
    {
        var vm = await OpenAsync();

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.Equal("the gadget is a gadget", File.ReadAllText(Path.Combine(_vault.Root, "Alpha.md")));
        Assert.Equal("one gadget here", File.ReadAllText(Path.Combine(_vault.Root, "sub", "Beta.md")));
        Assert.Equal(2, Assert.Single(_applied).NotesChanged);
        Assert.True(vm.CanUndo);
        Assert.Empty(vm.Results); // nothing says "widget" any more
    }

    [AvaloniaFact]
    public async Task Undo_puts_every_changed_note_back()
    {
        var vm = await OpenAsync();
        await vm.ReplaceAllCommand.ExecuteAsync(null);

        await vm.UndoReplaceCommand.ExecuteAsync(null);

        Assert.Equal("the widget is a widget", File.ReadAllText(Path.Combine(_vault.Root, "Alpha.md")));
        Assert.Equal("one widget here", File.ReadAllText(Path.Combine(_vault.Root, "sub", "Beta.md")));
        Assert.False(vm.CanUndo);
        Assert.Equal(3, vm.Results.Sum(r => r.Matches.Count));
    }

    [AvaloniaFact]
    public async Task Undo_is_unavailable_before_a_replace_and_after_a_vault_switch()
    {
        var vm = await OpenAsync();
        Assert.False(vm.CanUndo);

        await vm.ReplaceAllCommand.ExecuteAsync(null);
        Assert.True(vm.CanUndo);

        vm.ClearUndo();
        Assert.False(vm.CanUndo);
    }

    [AvaloniaFact]
    public async Task Opening_a_note_closes_the_overlay()
    {
        var vm = await OpenAsync();

        vm.OpenNoteCommand.Execute(vm.Results.Single(r => r.Name == "Beta"));

        Assert.False(vm.IsOpen);
        Assert.Equal(Path.Combine(_vault.Root, "sub", "Beta.md"), Assert.Single(_opened));
    }

    [AvaloniaFact]
    public async Task Close_empties_the_results()
    {
        var vm = await OpenAsync();

        vm.Close();

        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Results);
        Assert.Empty(vm.Status);
    }

    [AvaloniaFact]
    public async Task A_query_that_matches_nothing_says_so()
    {
        var vm = await OpenAsync("zzz-no-match");

        Assert.Empty(vm.Results);
        Assert.Equal("No matches", vm.Status);
        Assert.False(vm.CanReplace);
    }
}
