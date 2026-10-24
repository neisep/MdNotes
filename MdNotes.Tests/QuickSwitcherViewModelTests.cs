using MdNotes.ViewModels;

namespace MdNotes.Tests;

public sealed class QuickSwitcherViewModelTests : IDisposable
{
    private readonly TempVault _vault = new();
    private QuickSwitcherViewModel ViewModel { get; }

    public QuickSwitcherViewModelTests()
    {
        ViewModel = new QuickSwitcherViewModel(
            _vault.Service,
            _ => { },
            _ => { });
    }

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void Excluded_notes_are_not_offered()
    {
        _vault.Note("Plan.md");
        _vault.Note("node_modules/Plan.md");
        _vault.Ignore("node_modules");

        ViewModel.Open();

        Assert.Equal(["Plan"], ViewModel.Results.Select(r => r.Name));
    }

    [Fact]
    public void Empty_query_lists_all_notes_sorted_by_name()
    {
        _vault.Note("Plan.md");
        _vault.Note("Ideas/Planning.md");
        _vault.Note("Notes/Explanation.md");

        ViewModel.Open();

        Assert.Equal(3, ViewModel.Results.Count);
        Assert.Equal(new[] { "Explanation", "Plan", "Planning" },
            ViewModel.Results.Select(r => r.Name));
    }

    [Fact]
    public void Query_ranks_exact_match_first()
    {
        _vault.Note("Plan.md");
        _vault.Note("Ideas/Planning.md");
        _vault.Note("Notes/Explanation.md");

        ViewModel.Query = "plan";

        Assert.True(ViewModel.Results.Count >= 2);
        Assert.Equal("Plan", ViewModel.Results[0].Name); // Exact match (case-insensitive)
        Assert.Equal("Planning", ViewModel.Results[1].Name); // Starts with
    }

    [Fact]
    public void Query_ranks_starts_with_before_contains()
    {
        _vault.Note("Plan.md");
        _vault.Note("Ideas/Planning.md");
        _vault.Note("Notes/Explanation.md");

        ViewModel.Query = "plan";

        // Exact match > starts with > contains
        var names = ViewModel.Results.Select(r => r.Name).ToList();
        var planIndex = names.IndexOf("Plan");
        var planningIndex = names.IndexOf("Planning");
        var explanationIndex = names.IndexOf("Explanation");

        Assert.True(planIndex < planningIndex);
        Assert.True(planningIndex < explanationIndex);
    }

    [Fact]
    public void Query_without_diacritics_ranks_the_accented_note_first()
    {
        _vault.Note("Jönköping.md");
        _vault.Note("Trips/Jönköping notes.md");
        _vault.Note("Other/About Jönköping.md");

        ViewModel.Query = "jonkoping";

        var names = ViewModel.Results.Select(r => r.Name).ToList();
        Assert.Equal("Jönköping", names[0]);                 // exact, after folding
        Assert.Equal("Jönköping notes", names[1]);           // starts with
        Assert.Equal("About Jönköping", names[2]);           // contains
    }

    [Fact]
    public void Typing_the_diacritics_still_ranks_the_note_first()
    {
        _vault.Note("Jönköping.md");
        _vault.Note("Plan.md");

        ViewModel.Query = "Jönköping";

        Assert.Equal("Jönköping", ViewModel.Results[0].Name);
    }

    // The switcher used to fold both sides before ranking, so these two tied and the alphabetical
    // tie-break decided - while a wikilink to either resolved to its own file. One rule now, so the
    // note the user actually named is the one on top.
    [Fact]
    public void An_exact_name_outranks_one_that_only_matches_folded()
    {
        _vault.Note("Résumé.md");
        _vault.Note("Resume.md");

        ViewModel.Query = "Resume";
        Assert.Equal("Resume", ViewModel.Results[0].Name);

        ViewModel.Query = "Résumé";
        Assert.Equal("Résumé", ViewModel.Results[0].Name);
    }

    [Fact]
    public void Create_item_keeps_the_accents_the_user_typed()
    {
        _vault.Note("Plan.md");

        ViewModel.Query = "Skärgården";

        var item = Assert.Single(ViewModel.Results);
        Assert.True(item.IsCreate);
        Assert.Equal("Skärgården", item.Name);
    }

    [Fact]
    public void No_match_yields_create_item()
    {
        _vault.Note("Plan.md");
        _vault.Note("Ideas/Planning.md");

        ViewModel.Query = "zzz";

        Assert.Single(ViewModel.Results);
        var item = ViewModel.Results[0];
        Assert.True(item.IsCreate);
        Assert.Equal("zzz", item.Name);
        Assert.Null(item.FullPath);
    }

    [Fact]
    public void Create_item_has_trimmed_query()
    {
        _vault.Note("Plan.md");

        ViewModel.Query = "  new note  ";

        var createItem = ViewModel.Results.FirstOrDefault(r => r.IsCreate);
        Assert.NotNull(createItem);
        Assert.Equal("new note", createItem.Name);
    }

    [Fact]
    public void OpenSelected_on_create_item_invokes_callback()
    {
        _vault.Note("Plan.md");
        var createdName = "";
        var vm = new QuickSwitcherViewModel(
            _vault.Service,
            _ => { },
            name => createdName = name);

        vm.Query = "NewNote";
        vm.OpenSelected();

        Assert.Equal("NewNote", createdName);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void OpenSelected_on_note_item_invokes_callback()
    {
        var path = _vault.Note("Plan.md");
        var openedPath = "";
        var vm = new QuickSwitcherViewModel(
            _vault.Service,
            p => openedPath = p,
            _ => { });

        vm.Open();
        vm.Query = "plan";
        vm.OpenSelected();

        Assert.Equal(path, openedPath);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void MoveSelection_moves_within_results()
    {
        _vault.Note("A.md");
        _vault.Note("B.md");
        _vault.Note("C.md");

        ViewModel.Open();
        var first = ViewModel.Selected;

        ViewModel.MoveSelection(1);
        var second = ViewModel.Selected;

        ViewModel.MoveSelection(1);
        var third = ViewModel.Selected;

        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
    }

    [Fact]
    public void MoveSelection_clamps_at_end()
    {
        _vault.Note("A.md");
        _vault.Note("B.md");

        ViewModel.Open();
        ViewModel.MoveSelection(100);

        var last = ViewModel.Selected;
        ViewModel.MoveSelection(1);

        Assert.Same(last, ViewModel.Selected);
    }

    [Fact]
    public void MoveSelection_clamps_at_start()
    {
        _vault.Note("A.md");
        _vault.Note("B.md");

        ViewModel.Open();
        ViewModel.Selected = ViewModel.Results.Last();
        ViewModel.MoveSelection(-100);

        var first = ViewModel.Selected;
        ViewModel.MoveSelection(-1);

        Assert.Same(first, ViewModel.Selected);
    }

    [Fact]
    public void Results_are_capped_at_50()
    {
        // Create 60 notes
        for (int i = 0; i < 60; i++)
            _vault.Note($"Note{i:D2}.md");

        ViewModel.Open();

        Assert.Equal(50, ViewModel.Results.Count);
    }

    [Fact]
    public void Open_clears_query_and_lists_all_notes()
    {
        _vault.Note("Plan.md");
        _vault.Note("Ideas/Planning.md");

        ViewModel.Query = "plan";
        ViewModel.Open();

        // After open, query should be empty and results should include all notes
        Assert.Equal(string.Empty, ViewModel.Query);
        Assert.Equal(2, ViewModel.Results.Count);
    }

    [Fact]
    public void Close_sets_IsOpen_false()
    {
        ViewModel.Open();
        Assert.True(ViewModel.IsOpen);

        ViewModel.Close();
        Assert.False(ViewModel.IsOpen);
    }

    [Fact]
    public void OpenSelected_when_selected_is_null_does_nothing()
    {
        _vault.Note("Plan.md");

        ViewModel.Open();
        ViewModel.Results.Clear();
        ViewModel.Selected = null;

        ViewModel.OpenSelected(); // Should not throw

        Assert.False(ViewModel.IsOpen);
    }

    [Fact]
    public void Folder_path_is_correct_for_nested_notes()
    {
        _vault.Note("Ideas/Planning.md");
        _vault.Note("Notes/Deep/Explanation.md");

        ViewModel.Open();

        var planning = ViewModel.Results.First(r => r.Name == "Planning");
        var explanation = ViewModel.Results.First(r => r.Name == "Explanation");

        Assert.Equal("Ideas", planning.Folder);
        Assert.Equal("Notes/Deep", explanation.Folder);
    }

    [Fact]
    public void Root_notes_have_empty_folder()
    {
        _vault.Note("Plan.md");

        ViewModel.Open();

        var plan = ViewModel.Results.First(r => r.Name == "Plan");
        Assert.Equal("", plan.Folder);
    }

    /// <summary>
    /// Finding 5: the rows are flattened once per tree, not per keystroke. The vault hands back the
    /// same tree until it changes, so the same row instance comes back - and a new note means a new tree.
    /// </summary>
    [Fact]
    public void Rows_are_reused_across_keystrokes_until_the_vault_changes()
    {
        _vault.Note("Plan.md");
        ViewModel.Open();

        ViewModel.Query = "pl";
        var first = Assert.Single(ViewModel.Results);
        ViewModel.Query = "pla";
        Assert.Same(first, Assert.Single(ViewModel.Results));

        _vault.Note("Planning.md");
        ViewModel.Query = "plan";
        Assert.Equal(["Plan", "Planning"], ViewModel.Results.Select(r => r.Name));
    }

    /// <summary>
    /// The bounded top-50 selection must list exactly what sorting every match did: rank first, then
    /// name, then tree order - over more matches than fit, across every rank tier including the folded ones.
    /// </summary>
    [Fact]
    public void The_fifty_best_are_the_ones_a_full_sort_would_pick()
    {
        string[] shapes = ["plan{0}", "Plän{0}", "x plan {0}", "x plän {0}", "Plan", "Plän", "zz{0}"];
        for (var i = 0; i < 40; i++)
            foreach (var shape in shapes)
                _vault.Note($"f{i % 5}/{string.Format(shape, i)}.md");
        ViewModel.Open();

        ViewModel.Query = "plan";

        var expected = _vault.Service.GetNotes()
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => (name: name!, rank: Services.NameMatch.Rank(name!, "plan")))
            .Where(x => x.rank != Services.NameRank.None)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.name)
            .Select(x => x.name)
            .Take(50);
        Assert.Equal(expected, ViewModel.Results.Select(r => r.Name));
    }
}
