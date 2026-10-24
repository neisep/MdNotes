using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

public sealed class VaultServiceTests : IDisposable
{
    private readonly TempVault _vault = new();
    private VaultService Service => _vault.Service;

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void Open_rejects_missing_folder() =>
        Assert.Throws<DirectoryNotFoundException>(() => new VaultService(_vault.Settings).Open(Path.Combine(_vault.Root, "nope")));

    [Fact]
    public void GetTree_lists_folders_first_then_notes_and_hides_dot_folders()
    {
        _vault.Note("zeta.md");
        _vault.Note("Alpha.md");
        _vault.Note("docs/inner.md");
        _vault.Folder(".obsidian");
        File.WriteAllText(Path.Combine(_vault.Root, "ignored.txt"), "");

        var tree = Service.GetTree();

        Assert.Equal(["docs", "Alpha", "zeta"], tree.Select(e => e.Name));
        Assert.True(tree[0].IsFolder);
        Assert.Equal("inner", Assert.Single(tree[0].Children).Name);
    }

    [Fact]
    public void CreateNote_appends_extension_and_rejects_duplicates()
    {
        var path = Service.CreateNote(_vault.Root, "Ideas");

        Assert.Equal(Path.Combine(_vault.Root, "Ideas.md"), path);
        Assert.True(File.Exists(path));
        Assert.Throws<IOException>(() => Service.CreateNote(_vault.Root, "Ideas.md"));
    }

    /// <summary>
    /// Windows and macOS ignore case, so the two would be one file there. Rejected on every platform,
    /// because a vault made on Linux is meant to be synced to them.
    /// </summary>
    [Fact]
    public void CreateNote_rejects_a_duplicate_that_differs_only_in_case()
    {
        Service.CreateNote(_vault.Root, "Ideas");

        Assert.Throws<IOException>(() => Service.CreateNote(_vault.Root, "ideas.md"));
    }

    [Fact]
    public void CreateNote_writes_the_content_it_is_given_in_one_go()
    {
        var path = Service.CreateNote(_vault.Root, "Ideas", "# Ideas\n");

        Assert.Equal("# Ideas\n", File.ReadAllText(path));
    }

    [Fact]
    public void CreateNote_writes_nothing_when_it_is_given_nothing()
    {
        var path = Service.CreateNote(_vault.Root, "Ideas");

        Assert.Equal(string.Empty, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a\"b")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("name.")]
    [InlineData("CON")]
    [InlineData("con.md")]
    [InlineData("nul.backup")]
    [InlineData("LPT1")]
    [InlineData("com9")]
    public void CreateNote_rejects_blank_or_invalid_names(string name) =>
        Assert.Throws<ArgumentException>(() => Service.CreateNote(_vault.Root, name));

    /// <summary>':' is invalid in a Windows file name. Legal on Linux, but rejected there too: a vault is synced.</summary>
    [Fact]
    public void CreateNote_rejects_a_colon_in_the_name() =>
        Assert.Throws<ArgumentException>(() => Service.CreateNote(_vault.Root, "a:b"));

    /// <summary>Only the whole stem is a device name; names that merely start with one are ordinary.</summary>
    [Theory]
    [InlineData("Console")]
    [InlineData("com10")]
    [InlineData("auxiliary.notes")]
    [InlineData("a.b")]
    public void CreateNote_accepts_names_that_only_resemble_reserved_ones(string name) =>
        Assert.True(File.Exists(Service.CreateNote(_vault.Root, name)));

    [Theory]
    [InlineData("..")]
    [InlineData("aux")]
    [InlineData("a:b")]
    public void CreateFolder_rejects_names_that_are_not_portable(string name) =>
        Assert.Throws<ArgumentException>(() => Service.CreateFolder(_vault.Root, name));

    [Fact]
    public void CreateFolder_rejects_a_duplicate_that_differs_only_in_case()
    {
        Service.CreateFolder(_vault.Root, "Projects");

        Assert.Throws<IOException>(() => Service.CreateFolder(_vault.Root, "projects"));
    }

    [Fact]
    public void CreateFolder_creates_and_rejects_duplicates()
    {
        var path = Service.CreateFolder(_vault.Root, "Projects");

        Assert.True(Directory.Exists(path));
        Assert.Throws<IOException>(() => Service.CreateFolder(_vault.Root, "Projects"));
    }

    [Fact]
    public void Rename_moves_note_and_keeps_extension()
    {
        var old = _vault.Note("Old.md", "body");

        var renamed = Service.Rename(old, "New");

        Assert.Equal(Path.Combine(_vault.Root, "New.md"), renamed);
        Assert.False(File.Exists(old));
        Assert.Equal("body", File.ReadAllText(renamed));
    }

    [Fact]
    public void Rename_moves_folder_with_contents()
    {
        var folder = _vault.Folder("a");
        _vault.Note("a/x.md", "x");

        var renamed = Service.Rename(folder, "b");

        Assert.Equal(Path.Combine(_vault.Root, "b"), renamed);
        Assert.True(File.Exists(Path.Combine(renamed, "x.md")));
    }

    [Fact]
    public void Rename_rejects_existing_target()
    {
        var a = _vault.Note("a.md");
        _vault.Note("b.md");

        Assert.Throws<IOException>(() => Service.Rename(a, "b"));
    }

    [Fact]
    public void Rename_rejects_a_name_that_is_not_portable()
    {
        var a = _vault.Note("a.md");

        Assert.Throws<ArgumentException>(() => Service.Rename(a, "a:b"));
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void Rename_rejects_a_sibling_that_differs_only_in_case()
    {
        var a = _vault.Note("a.md");
        _vault.Note("Ideas.md");

        Assert.Throws<IOException>(() => Service.Rename(a, "ideas"));
    }

    /// <summary>The clash check skips the entry itself, so fixing the case of a name stays possible.</summary>
    [Fact]
    public void Rename_can_change_only_the_case_of_a_name()
    {
        var note = _vault.Note("ideas.md");
        var folder = _vault.Folder("projects");

        var renamedNote = Service.Rename(note, "Ideas");
        var renamedFolder = Service.Rename(folder, "Projects");

        Assert.Equal("Ideas.md", Path.GetFileName(Directory.GetFiles(_vault.Root).Single()));
        Assert.Equal("Projects", Path.GetFileName(renamedFolder));
        Assert.Equal(Path.Combine(_vault.Root, "Ideas.md"), renamedNote);
    }

    [Fact]
    public void Delete_removes_folder_recursively_and_throws_when_missing()
    {
        var folder = _vault.Folder("bin");
        _vault.Note("bin/deep/note.md");

        Service.Delete(folder);

        Assert.False(Directory.Exists(folder));
        Assert.Throws<FileNotFoundException>(() => Service.Delete(folder));
    }

    [Fact]
    public void FindNoteByName_is_case_insensitive_and_searches_subfolders()
    {
        var path = _vault.Note("deep/er/Meeting Notes.md");

        Assert.Equal(path, Service.FindNoteByName("meeting notes"));
        Assert.Null(Service.FindNoteByName("missing"));
    }

    [Fact]
    public void FindNoteByName_ignores_diacritics()
    {
        var path = _vault.Note("Jönköping.md");

        Assert.Equal(path, Service.FindNoteByName("jonkoping"));
        Assert.Equal(path, Service.FindNoteByName("Jönköping"));
    }

    [Fact]
    public void FindNoteByName_prefers_an_exact_name_over_a_folded_one()
    {
        var plain = _vault.Note("Resume.md");
        var accented = _vault.Note("Résumé.md");

        Assert.Equal(plain, Service.FindNoteByName("Resume"));
        Assert.Equal(accented, Service.FindNoteByName("Résumé"));
    }

    [Fact]
    public void FindFolderByName_is_case_insensitive_and_searches_subfolders()
    {
        var folder = _vault.Folder("deep/er/Project Notes");

        Assert.Equal(folder, Service.FindFolderByName("project notes"));
        Assert.Null(Service.FindFolderByName("missing"));
    }

    [Fact]
    public void FindFolderByName_ignores_diacritics()
    {
        var folder = _vault.Folder("Jönköping");

        Assert.Equal(folder, Service.FindFolderByName("jonkoping"));
        Assert.Equal(folder, Service.FindFolderByName("Jönköping"));
    }

    [Fact]
    public void FindFolderByName_prefers_an_exact_name_over_a_folded_one()
    {
        var plain = _vault.Folder("Resume");
        var accented = _vault.Folder("Résumé");

        Assert.Equal(plain, Service.FindFolderByName("Resume"));
        Assert.Equal(accented, Service.FindFolderByName("Résumé"));
    }

    [Fact]
    public void FindFolderByName_matches_a_vault_relative_path_when_the_name_has_separators()
    {
        _vault.Folder("Work/Projects");
        var other = _vault.Folder("Home/Projects");

        Assert.Equal(other, Service.FindFolderByName("Home/Projects"));
        Assert.Equal(other, Service.FindFolderByName("Home\\Projects"));
        Assert.Null(Service.FindFolderByName("Nowhere/Projects"));
    }

    [Fact]
    public void FindFolderByName_never_returns_a_dot_folder_or_anything_inside_one()
    {
        _vault.Folder(".trash");
        _vault.Folder(".trash/Projects");

        Assert.Null(Service.FindFolderByName(".trash"));
        Assert.Null(Service.FindFolderByName("Projects"));
    }

    [Fact]
    public void Write_then_Read_round_trips()
    {
        var path = _vault.Note("n.md");

        Service.Write(path, "# Title\n");

        Assert.Equal("# Title\n", Service.Read(path));
    }

    /// <summary>
    /// Saving twice in the same tick at the same length is what an editor does, and it is the one case
    /// the write stamp cannot see. Writes go into the cache directly, so the editor always reads back
    /// what it just saved rather than the version before it.
    /// </summary>
    [Fact]
    public void Read_returns_the_last_write_even_when_the_stamp_cannot_tell()
    {
        var path = _vault.Note("n.md", "aaaaa");
        Service.Read(path);

        Service.Write(path, "bbbbb");

        Assert.Equal("bbbbb", Service.Read(path));
    }

    /// <summary>A note read from one vault must not answer for the same path in another.</summary>
    [Fact]
    public void Opening_another_vault_forgets_what_was_read_from_this_one()
    {
        var path = _vault.Note("n.md", "first vault");
        var held = Service.Read(path);

        using var other = new TempVault();
        Service.Open(other.Root);
        Service.Open(_vault.Root);

        Assert.NotSame(held, Service.Read(path));
        Assert.Equal("first vault", Service.Read(path));
    }

    [Fact]
    public void GetTree_follows_the_order_file_and_lets_a_note_precede_a_folder()
    {
        _vault.Note("Intro.md");
        _vault.Note("Methods.md");
        _vault.Folder("Chapters");
        WriteOrder(_vault.Root, "Intro.md", "Chapters/", "Methods.md");

        Assert.Equal(["Intro", "Chapters", "Methods"], Service.GetTree().Select(e => e.Name));
    }

    [Fact]
    public void GetTree_ignores_stale_names_and_puts_unlisted_entries_last()
    {
        _vault.Note("Zeta.md");
        _vault.Note("Alpha.md");
        _vault.Note("Listed.md");
        WriteOrder(_vault.Root, "Listed.md", "Deleted.md");

        Assert.Equal(["Listed", "Alpha", "Zeta"], Service.GetTree().Select(e => e.Name));
    }

    [Fact]
    public void MoveTo_writes_the_whole_sibling_order()
    {
        _vault.Note("a.md");
        _vault.Note("b.md");
        _vault.Note("c.md");

        Service.MoveTo(Path.Combine(_vault.Root, "c.md"), _vault.Root, 0);

        Assert.Equal(["c.md", "a.md", "b.md"], ReadOrder(_vault.Root));
        Assert.Equal(["c", "a", "b"], Service.GetTree().Select(e => e.Name));
    }

    [Fact]
    public void Rename_keeps_the_manual_position()
    {
        _vault.Note("a.md");
        _vault.Note("b.md");
        _vault.Note("c.md");
        Service.MoveTo(Path.Combine(_vault.Root, "a.md"), _vault.Root, 2);

        Service.Rename(Path.Combine(_vault.Root, "b.md"), "zzz");

        Assert.Equal(["zzz", "c", "a"], Service.GetTree().Select(e => e.Name));
    }

    [Fact]
    public void MoveTo_moves_between_folders_and_prunes_the_source_order()
    {
        _vault.Note("a.md");
        _vault.Note("b.md");
        var box = _vault.Folder("Box");
        _vault.Note("Box/x.md");
        Service.MoveTo(Path.Combine(_vault.Root, "b.md"), _vault.Root, 0);

        var moved = Service.MoveTo(Path.Combine(_vault.Root, "a.md"), box, 0);

        Assert.Equal(Path.Combine(box, "a.md"), moved);
        Assert.Equal(["a.md", "x.md"], ReadOrder(box));
        Assert.DoesNotContain("a.md", ReadOrder(_vault.Root));
    }

    [Fact]
    public void MoveTo_rejects_a_folder_into_its_own_subtree()
    {
        var outer = _vault.Folder("outer");
        var inner = _vault.Folder("outer/inner");

        Assert.Throws<IOException>(() => Service.MoveTo(outer, inner, 0));
    }

    [Fact]
    public void MoveTo_rejects_a_name_clash_in_the_destination()
    {
        var note = _vault.Note("dup.md");
        var box = _vault.Folder("Box");
        _vault.Note("Box/dup.md");

        Assert.Throws<IOException>(() => Service.MoveTo(note, box, 0));
    }

    [Fact]
    public void MoveTo_rejects_a_clash_in_the_destination_that_differs_only_in_case()
    {
        var note = _vault.Note("dup.md");
        var box = _vault.Folder("Box");
        _vault.Note("Box/DUP.md");

        Assert.Throws<IOException>(() => Service.MoveTo(note, box, 0));
        Assert.True(File.Exists(note));
    }

    [Fact]
    public void ResetOrder_restores_alphabetical_order()
    {
        _vault.Note("a.md");
        _vault.Note("b.md");
        Service.MoveTo(Path.Combine(_vault.Root, "b.md"), _vault.Root, 0);
        Assert.Equal(["b", "a"], Service.GetTree().Select(e => e.Name));

        Service.ResetOrder(_vault.Root);

        Assert.Equal(["a", "b"], Service.GetTree().Select(e => e.Name));
        Assert.False(File.Exists(Path.Combine(_vault.Root, ".order")));
    }

    [Fact]
    public void GetTree_leaves_out_excluded_folders_and_notes()
    {
        _vault.Note("Keep.md");
        _vault.Note("scratch.tmp.md");
        _vault.Note("node_modules/left/pad.md");
        _vault.Folder("Archive");
        _vault.Ignore("node_modules\n*.tmp.md\nArchive/");

        Assert.Equal(["Keep"], Service.GetTree().Select(e => e.Name));
    }

    /// <summary>
    /// The note list was once its own walk and never skipped dot-folders the way the tree did, so
    /// search read '.git' and a wikilink could resolve to a note with no row to select. One walk now
    /// answers both, so the agreement is structural - this pins it anyway.
    /// </summary>
    [Fact]
    public void GetNotes_leaves_out_excluded_and_dot_folders()
    {
        _vault.Note("Keep.md");
        _vault.Note(".git/modules/Hidden.md");
        _vault.Note("node_modules/left/pad.md");
        _vault.Ignore("node_modules");

        Assert.Equal(["Keep"], Service.GetNotes().Select(Path.GetFileNameWithoutExtension));
    }

    [Fact]
    public void FindNoteByName_does_not_resolve_to_an_excluded_note()
    {
        _vault.Note("node_modules/Readme.md");
        _vault.Ignore("node_modules");

        Assert.Null(Service.FindNoteByName("Readme"));
    }

    [Fact]
    public void FindFolderByName_does_not_resolve_to_an_excluded_folder()
    {
        _vault.Folder("node_modules/left");
        _vault.Ignore("node_modules");

        Assert.Null(Service.FindFolderByName("left"));
        Assert.Null(Service.FindFolderByName("node_modules"));
    }

    /// <summary>
    /// '.order' records every sibling, excluded or not. If a drag rewrote it from the visible rows,
    /// un-excluding an entry later would silently have moved it to the bottom of its folder.
    /// </summary>
    [Fact]
    public void An_excluded_sibling_keeps_its_place_in_the_order_file()
    {
        _vault.Note("a.md");
        _vault.Note("b.md");
        _vault.Note("c.md");
        WriteOrder(_vault.Root, "c.md", "b.md", "a.md");
        _vault.Ignore("b.md");

        Service.MoveTo(Path.Combine(_vault.Root, "a.md"), _vault.Root, 0);

        Assert.Equal(["a.md", "c.md", "b.md"], ReadOrder(_vault.Root));
    }

    /// <summary>The sidebar counts the rows it drew; '.order' counts everything on disk.</summary>
    [Fact]
    public void MoveTo_counts_the_index_among_visible_children()
    {
        _vault.Note("hidden.md");
        _vault.Note("a.md");
        _vault.Note("b.md");
        WriteOrder(_vault.Root, "hidden.md", "a.md", "b.md");
        _vault.Ignore("hidden.md");

        Service.MoveTo(Path.Combine(_vault.Root, "b.md"), _vault.Root, 0);

        Assert.Equal(["b", "a"], Service.GetTree().Select(e => e.Name));
        Assert.Contains("hidden.md", ReadOrder(_vault.Root));
    }

    [Fact]
    public void CreateNote_refuses_a_name_the_exclusions_would_hide()
    {
        _vault.Ignore("Draft*");

        Assert.Throws<IOException>(() => Service.CreateNote(_vault.Root, "Draft one"));
    }

    [Fact]
    public void CreateFolder_refuses_a_name_the_exclusions_would_hide()
    {
        _vault.Ignore("node_modules");

        Assert.Throws<IOException>(() => Service.CreateFolder(_vault.Root, "node_modules"));
    }

    /// <summary>Renaming something already excluded is how it is brought back into view.</summary>
    [Fact]
    public void Rename_refuses_to_hide_a_visible_note_but_not_to_reveal_a_hidden_one()
    {
        var visible = _vault.Note("Visible.md");
        var hidden = _vault.Note("secret.tmp.md");
        _vault.Ignore("*.tmp.md");

        Assert.Throws<IOException>(() => Service.Rename(visible, "gone.tmp"));
        Assert.Equal(Path.Combine(_vault.Root, "Shown.md"), Service.Rename(hidden, "Shown"));
    }

    [Fact]
    public void The_exclusion_list_is_re_read_when_the_file_changes()
    {
        _vault.Note("Keep.md");
        _vault.Note("Gone.md");
        var stale = Service.GetTree();
        Assert.Equal(2, stale.Count);

        File.WriteAllText(Path.Combine(_vault.Root, ".mdnotesignore"), "Gone.md");

        // Nothing announced this write, so the cached walk is only dropped because the compiled list
        // it was made with is no longer the one the vault hands out. A revision counter alone misses it.
        Assert.NotSame(stale, Service.GetTree());
        Assert.Equal(["Keep"], Service.GetTree().Select(e => e.Name));
    }

    [Fact]
    public void The_gitignore_counts_only_when_the_setting_says_so()
    {
        _vault.Note("Keep.md");
        _vault.Note("build/out.md");
        File.WriteAllText(Path.Combine(_vault.Root, ".gitignore"), "build/");
        Service.RefreshExclusions();

        Assert.Equal(2, Service.GetTree().Count);

        _vault.Settings.UseGitIgnore = true;
        Service.RefreshExclusions();

        Assert.Equal(["Keep"], Service.GetTree().Select(e => e.Name));
    }

    [Fact]
    public void RefreshExclusions_raises_VaultChanged()
    {
        var raised = 0;
        Service.VaultChanged += (_, _) => raised++;

        Service.RefreshExclusions();

        Assert.Equal(1, raised);
    }

    private static void WriteOrder(string folder, params string[] keys) =>
        File.WriteAllLines(Path.Combine(folder, ".order"), keys);

    private static string[] ReadOrder(string folder) => File.ReadAllLines(Path.Combine(folder, ".order"));

    [Fact]
    public async Task VaultChanged_fires_when_a_file_is_created_externally()
    {
        var signal = new TaskCompletionSource();
        Service.VaultChanged += (_, _) => signal.TrySetResult();

        _vault.Note("external.md");

        var finished = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(signal.Task, finished);
    }

    [Fact]
    public void Open_announces_a_switch_but_says_nothing_about_the_vault_it_already_has()
    {
        var opened = 0;
        Service.VaultOpened += (_, _) => opened++;

        // TempVault already opened this root. Every new window restores the last vault, so re-opening
        // it is the common case, and announcing it would reset the windows already showing it.
        Assert.False(Service.Open(_vault.Root));
        Assert.Equal(0, opened);

        var other = Directory.CreateTempSubdirectory("mdnotes-other-").FullName;
        try
        {
            Assert.True(Service.Open(other));
            Assert.Equal(1, opened);
            Assert.Equal(other, Service.RootPath);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    /// <summary>
    /// Four call sites each walked the vault on demand, one of them on every keystroke in the search
    /// box. The second read has to be the same answer, not the same answer walked again.
    /// </summary>
    [Fact]
    public void Two_reads_in_a_row_answer_from_the_same_walk()
    {
        _vault.Note("Alpha.md");
        _vault.Note("Box/Inner.md");

        using var settled = SettledVault();

        Assert.Same(settled.GetTree(), settled.GetTree());
        Assert.Same(settled.GetNotes(), settled.GetNotes());
    }

    /// <summary>
    /// Every caller rebuilds the sidebar synchronously the moment a mutation returns, long before the
    /// watcher reports it - so a cache waiting on the watcher hid the note the user had just made.
    /// </summary>
    [Fact]
    public void A_new_note_is_in_the_very_next_tree()
    {
        var stale = Service.GetTree();

        var path = Service.CreateNote(_vault.Root, "Fresh");

        Assert.NotSame(stale, Service.GetTree());
        Assert.Equal(["Fresh"], Service.GetTree().Select(e => e.Name));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Every_structural_change_is_visible_to_the_next_read()
    {
        var box = Service.CreateFolder(_vault.Root, "Box");
        Assert.Equal(["Box"], Service.GetTree().Select(e => e.Name));

        var note = Service.CreateNote(_vault.Root, "Alpha");
        Assert.Equal(["Box", "Alpha"], Service.GetTree().Select(e => e.Name));

        var renamed = Service.Rename(note, "Beta");
        Assert.Equal(["Box", "Beta"], Service.GetTree().Select(e => e.Name));

        Service.MoveTo(renamed, _vault.Root, 0);
        Assert.Equal(["Beta", "Box"], Service.GetTree().Select(e => e.Name));

        Service.ResetOrder(_vault.Root);
        Assert.Equal(["Box", "Beta"], Service.GetTree().Select(e => e.Name));

        Service.Delete(box);
        Assert.Equal(["Beta"], Service.GetTree().Select(e => e.Name));
    }

    /// <summary>Autosave calls Write on a timer; walking the vault for that would undo the whole point.</summary>
    [Fact]
    public void Writing_a_notes_content_does_not_walk_the_vault_again()
    {
        var path = _vault.Note("Alpha.md", "before");

        using var settled = SettledVault();
        var tree = settled.GetTree();

        settled.Write(path, "after");

        Assert.Same(tree, settled.GetTree());
        Assert.Equal("after", settled.Read(path));
    }

    /// <summary>
    /// The note list used to be a walk of its own, which is how search came to read '.git' for notes
    /// the tree would never show. One walk answers both now; this says so in a test.
    /// </summary>
    [Fact]
    public void The_tree_and_the_note_list_come_from_the_one_walk()
    {
        _vault.Note("Keep.md");
        _vault.Note("Box/Inner.md");
        _vault.Note("Box/Draft.tmp.md");
        _vault.Note(".git/modules/Hidden.md");
        _vault.Note("node_modules/left/pad.md");
        _vault.Ignore("node_modules\n*.tmp.md");

        Assert.Equal(NotesIn(Service.GetTree()), Service.GetNotes());
        Assert.Equal(["Inner", "Keep"], Service.GetNotes().Select(Path.GetFileNameWithoutExtension).Order());
    }

    /// <summary>
    /// The list is the tree read top to bottom, so a search result list and the sidebar finally agree
    /// and a wikilink to a duplicated name resolves the same way twice.
    /// </summary>
    [Fact]
    public void Notes_are_listed_in_the_order_the_sidebar_shows_them()
    {
        _vault.Note("Zeta.md");
        _vault.Note("Alpha.md");
        _vault.Note("Box/Inner.md");
        WriteOrder(_vault.Root, "Zeta.md", "Box/", "Alpha.md");

        Assert.Equal(["Zeta", "Inner", "Alpha"], Service.GetNotes().Select(Path.GetFileNameWithoutExtension));
    }

    [Fact]
    public void Opening_another_vault_never_answers_from_the_old_ones_walk()
    {
        _vault.Note("Alpha.md");
        Assert.Equal(["Alpha"], Service.GetTree().Select(e => e.Name));

        var other = Directory.CreateTempSubdirectory("mdnotes-other-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(other, "Beta.md"), "");

            Assert.True(Service.Open(other));
            Assert.Equal(["Beta"], Service.GetTree().Select(e => e.Name));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    /// <summary>
    /// The one path where nothing tells the vault: invalidating before VaultChanged is raised, never
    /// after, is what lets a handler read the vault synchronously and see the new note.
    /// </summary>
    [Fact]
    public async Task A_note_created_outside_the_app_is_in_the_tree_once_the_watcher_has_spoken()
    {
        Assert.Empty(Service.GetTree());

        var signal = new TaskCompletionSource();
        Service.VaultChanged += (_, _) => signal.TrySetResult();

        // Not TempVault.Note, which tells the vault itself - noticing this is the watcher's job.
        File.WriteAllText(Path.Combine(_vault.Root, "external.md"), "");

        var finished = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(signal.Task, finished);

        Assert.Equal(["external"], Service.GetTree().Select(e => e.Name));
    }

    /// <summary>
    /// A second vault opened over the already-seeded folder. Creating a file makes the watcher fire a
    /// moment later and drop the cached walk - which is the point of the watcher - so a test that
    /// wants to catch a needless re-walk has to ask a vault nothing is changing underneath.
    /// </summary>
    private VaultService SettledVault()
    {
        var service = new VaultService(_vault.Settings);
        service.Open(_vault.Root);
        return service;
    }

    /// <summary>Every note the tree holds, top to bottom - what GetNotes has to agree with.</summary>
    private static List<string> NotesIn(IReadOnlyList<VaultEntry> entries)
    {
        var notes = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.IsFolder) notes.AddRange(NotesIn(entry.Children));
            else notes.Add(entry.FullPath);
        }
        return notes;
    }
}
