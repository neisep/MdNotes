using MdNotes.Services;
using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// The declared title reaching the surfaces that show a note, and staying off the ones that identify
/// it. The split is the whole design: what is shown can change when someone edits a heading, what a
/// link resolves and a rename moves cannot.
/// </summary>
public sealed class NoteTitleDisplayTests : IDisposable
{
    private readonly TempVault _vault = new();

    public NoteTitleDisplayTests() => _vault.Settings.UseNoteTitles = true;

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void A_note_is_shown_under_its_frontmatter_title()
    {
        var path = _vault.Note("2026-09-21-draft.md", "---\ntitle: Q3 Planning Notes\n---\n\n# Quarterly\n");

        Assert.Equal("Q3 Planning Notes", _vault.Service.DisplayName(path));
        Assert.Equal("Q3 Planning Notes", Assert.Single(_vault.Service.GetTree()).Display);
    }

    [Fact]
    public void A_note_with_no_title_is_shown_under_its_file_name()
    {
        var path = _vault.Note("Plain.md", "no heading, no frontmatter");

        Assert.Equal("Plain", _vault.Service.DisplayName(path));
        Assert.Null(Assert.Single(_vault.Service.GetTree()).Title);
    }

    [Fact]
    public void Titles_are_not_read_at_all_when_the_setting_is_off()
    {
        _vault.Settings.UseNoteTitles = false;
        var path = _vault.Note("draft.md", "# A heading it declares\n");

        Assert.Equal("draft", _vault.Service.DisplayName(path));
        Assert.Null(Assert.Single(_vault.Service.GetTree()).Title);
    }

    /// <summary>
    /// The line the feature does not cross. A title is not unique and can change under you by someone
    /// editing a heading; a file name is the note's identity, so that is what a link resolves.
    /// </summary>
    [Fact]
    public void A_wikilink_still_resolves_by_file_name_not_by_title()
    {
        var path = _vault.Note("draft.md", "---\ntitle: Q3 Planning Notes\n---\n");

        Assert.Equal(path, _vault.Service.FindNoteByName("draft"));
        Assert.Null(_vault.Service.FindNoteByName("Q3 Planning Notes"));
    }

    /// <summary>Editing the heading has to reach the sidebar, which is the row that is already there.</summary>
    [Fact]
    public void Saving_a_new_title_changes_what_the_tree_shows()
    {
        var path = _vault.Note("draft.md", "# First heading\n");
        Assert.Equal("First heading", Assert.Single(_vault.Service.GetTree()).Display);

        _vault.Service.Write(path, "# Renamed heading\n");

        Assert.Equal("Renamed heading", Assert.Single(_vault.Service.GetTree()).Display);
    }

    /// <summary>
    /// A save that does not change the title must not rebuild the tree - that is a walk of the vault,
    /// and autosave runs it on a timer while someone types.
    /// </summary>
    [Fact]
    public void Saving_without_changing_the_title_leaves_the_tree_alone()
    {
        var path = _vault.Note("draft.md", "# Same heading\n\nbody");

        // A second service over the already-seeded folder, the way the other cache tests do it. The
        // first one still has the watcher's Created event for this note in flight, and it lands as an
        // invalidation a moment later - which is the watcher working, not the save rebuilding the
        // tree. That is what made this assertion fail about one full-suite run in four.
        using var settled = new VaultService(_vault.Settings);
        settled.Open(_vault.Root);
        var before = settled.GetTree();

        settled.Write(path, "# Same heading\n\nbody, now longer");

        Assert.Same(before, settled.GetTree());
    }

    [Fact]
    public void Search_results_are_named_the_way_the_sidebar_names_them()
    {
        _vault.Note("draft.md", "---\ntitle: Q3 Planning Notes\n---\n\nbudget and headcount\n");

        var hit = Assert.Single(new SearchService(_vault.Service).Search("headcount", TestContext.Current.CancellationToken));

        Assert.Equal("Q3 Planning Notes", hit.Name);
    }

    /// <summary>Titles must not make a note harder to reach: the file name still finds it.</summary>
    [Theory]
    [InlineData("Q3 Planning")]
    [InlineData("draft")]
    public void The_switcher_finds_a_note_by_its_title_or_its_file_name(string query)
    {
        var path = _vault.Note("draft.md", "---\ntitle: Q3 Planning Notes\n---\n");
        var switcher = new QuickSwitcherViewModel(_vault.Service, _ => { }, _ => { });

        switcher.Open();
        switcher.Query = query;

        Assert.Equal(path, Assert.Single(switcher.Results).FullPath);
        Assert.Equal("Q3 Planning Notes", switcher.Results[0].Name);
    }

    /// <summary>A note that cannot be read is a row with its file name, not a broken tree.</summary>
    [Fact]
    public void A_note_that_cannot_be_read_falls_back_to_its_file_name()
    {
        var path = _vault.Note("locked.md", "# Declared\n");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        // Windows enforces exclusive sharing; on Unix it is advisory and only emulated. Where the read
        // goes through there is no unreadable note to assert about, so there is nothing to test rather
        // than something to fail.
        Assert.SkipWhen(CanRead(path), "this platform does not enforce FileShare.None");

        _vault.Service.Invalidate();

        Assert.Equal("locked", Assert.Single(_vault.Service.GetTree()).Display);
    }

    private static bool CanRead(string path)
    {
        try
        {
            File.ReadAllText(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
