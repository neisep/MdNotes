using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// The content cache on its own: that a second read costs nothing, that an edit from outside is still
/// picked up, that what the app writes is trusted over the stamp, and that the character budget holds.
/// </summary>
public class NoteContentCacheTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mdnotes-content-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Note(string name, string text)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>
    /// ReadAllText hands back a new string every time, so the same instance twice is the cache serving
    /// it - no counter or seam needed to see that the disk was not touched.
    /// </summary>
    [Fact]
    public void The_same_file_is_read_once()
    {
        var cache = new NoteContentCache();
        var path = Note("a.md", "hello");

        Assert.Same(cache.Read(path), cache.Read(path));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void A_file_edited_outside_the_app_is_read_again()
    {
        var cache = new NoteContentCache();
        var path = Note("a.md", "before");
        cache.Read(path);

        File.WriteAllText(path, "after the edit");

        Assert.Equal("after the edit", cache.Read(path));
    }

    /// <summary>
    /// The case the stamp cannot catch: same length, and on a coarse clock possibly the same tick. The
    /// app's own save goes in directly rather than waiting to be noticed, so it can never serve the
    /// older text back to the editor that just wrote it.
    /// </summary>
    [Fact]
    public void Text_the_app_stores_is_served_even_when_the_stamp_could_not_tell()
    {
        var cache = new NoteContentCache();
        var path = Note("a.md", "aaaaa");
        cache.Read(path);

        File.WriteAllText(path, "bbbbb");
        cache.Store(path, "bbbbb");

        Assert.Equal("bbbbb", cache.Read(path));
    }

    /// <summary>Finding 3: the fold is made once per read of the file, not once per scan.</summary>
    [Fact]
    public void A_note_read_twice_folds_once()
    {
        var cache = new NoteContentCache();
        var path = Note("cv.md", "Updated my résumé");

        var first = cache.ReadFolded(path);
        var second = cache.ReadFolded(path);

        Assert.Equal("Updated my resume", first.Folded);
        Assert.Same(first.Folded, second.Folded);
        Assert.Same(first.Text, second.Text);
    }

    /// <summary>A fold is memory like the text is, so it counts - unless it is the text itself.</summary>
    [Fact]
    public void A_fold_counts_against_the_budget_only_when_it_is_a_copy()
    {
        var cache = new NoteContentCache();
        cache.ReadFolded(Note("plain.md", "plain"));
        Assert.Equal(5, cache.HeldChars);

        cache.ReadFolded(Note("cafe.md", "café"));
        Assert.Equal(5 + 4 + 4, cache.HeldChars);
    }

    /// <summary>What the app writes replaces the entry, fold and all; the next fold is of the new text.</summary>
    [Fact]
    public void A_stored_write_drops_the_old_fold()
    {
        var cache = new NoteContentCache();
        var path = Note("a.md", "café");
        cache.ReadFolded(path);

        File.WriteAllText(path, "naïve");
        cache.Store(path, "naïve");

        Assert.Equal("naive", cache.ReadFolded(path).Folded);
        Assert.Equal(5 + 5, cache.HeldChars);
    }

    /// <summary>
    /// Finding 21's signal: once the budget has had to evict, the vault is bigger than the cache, and a
    /// walk that leaned on it being warm no longer can. Forgotten with everything else on a vault switch.
    /// </summary>
    [Fact]
    public void Evicting_marks_the_cache_as_overrun_until_it_is_cleared()
    {
        var cache = new NoteContentCache(budgetChars: 20);
        cache.Read(Note("a.md", new string('a', 10)));
        cache.Read(Note("b.md", new string('b', 10)));
        Assert.False(cache.HasOverrun);

        cache.Read(Note("c.md", new string('c', 10)));
        Assert.True(cache.HasOverrun);

        cache.Clear();
        Assert.False(cache.HasOverrun);
    }

    [Fact]
    public void The_least_recently_read_note_is_dropped_when_the_budget_is_reached()
    {
        var cache = new NoteContentCache(budgetChars: 20);
        var first = Note("a.md", new string('a', 10));
        var second = Note("b.md", new string('b', 10));

        var held = cache.Read(first);
        cache.Read(second);
        cache.Read(second);          // second is now the more recently used of the two
        cache.Read(Note("c.md", new string('c', 10)));

        Assert.Equal(2, cache.Count);
        Assert.True(cache.HeldChars <= 20);
        Assert.NotSame(held, cache.Read(first));
    }

    /// <summary>A note bigger than the whole budget is still usable; it is the next read that trims it.</summary>
    [Fact]
    public void A_note_larger_than_the_budget_is_still_returned()
    {
        var cache = new NoteContentCache(budgetChars: 8);
        var big = new string('x', 64);

        Assert.Equal(big, cache.Read(Note("big.md", big)));
    }

    /// <summary>
    /// A hit moves the entry to the back of the recency list rather than re-adding it. Re-adding would
    /// count its characters twice, and the budget would then evict against a total that was never held.
    /// </summary>
    [Fact]
    public void Reading_a_held_note_again_does_not_change_what_is_held()
    {
        var cache = new NoteContentCache();
        var path = Note("a.md", new string('a', 100));

        cache.Read(path);
        var afterFirst = cache.HeldChars;
        cache.Read(path);
        cache.Read(path);

        Assert.Equal(100, afterFirst);
        Assert.Equal(afterFirst, cache.HeldChars);
        Assert.Equal(1, cache.Count);
    }

    /// <summary>
    /// The invariant the recency list has to keep: held characters are the characters of the entries
    /// still in the map. A scan far past the budget is what exercises it - that is the path where the
    /// eviction loop runs once per note read, and where a leaked or double-counted entry would show.
    /// </summary>
    [Fact]
    public void A_scan_far_past_the_budget_leaves_the_bookkeeping_exact()
    {
        const int size = 100;
        var cache = new NoteContentCache(budgetChars: size * 10);

        for (var i = 0; i < 200; i++)
            cache.Read(Note($"n{i}.md", new string('x', size)));

        Assert.Equal(10, cache.Count);
        Assert.Equal(cache.Count * size, cache.HeldChars);
        Assert.True(cache.HeldChars <= cache.BudgetChars);
    }

    /// <summary>
    /// The budget has to sit above a real vault, not below it: overrunning turns every search back into
    /// a full re-read, because a pass over every note evicts in the order it is about to ask again.
    /// Measured at ~2.7 KB a note, so this is the vault size the default is claimed to cover.
    /// </summary>
    [Fact]
    public void The_default_budget_holds_a_vault_of_ten_thousand_notes()
    {
        Assert.True(new NoteContentCache().BudgetChars >= 10_000 * 2_700);
    }

    /// <summary>A note with an accent is held twice, as text and as its fold, and still fits this many.</summary>
    [Fact]
    public void The_default_budget_holds_eight_thousand_accented_notes()
    {
        Assert.True(new NoteContentCache().BudgetChars >= 8_000 * 2 * 2_700);
    }

    /// <summary>
    /// The cache used to say "no lock: every caller is on the UI thread", and the vault-wide scans now
    /// run on the thread pool while the editor still reads and writes on the UI thread. Even a hit
    /// mutates - it moves the entry down the recency list - so unsynchronised this corrupted the
    /// dictionary rather than merely racing.
    /// </summary>
    [Fact]
    public async Task Readers_on_several_threads_leave_the_bookkeeping_exact()
    {
        const int size = 100;
        var cache = new NoteContentCache();
        var paths = Enumerable.Range(0, 200).Select(i => Note($"n{i}.md", new string('x', size))).ToArray();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            foreach (var path in paths)
                Assert.Equal(size, cache.Read(path).Length);
        })));

        Assert.Equal(paths.Length, cache.Count);
        Assert.Equal(paths.Length * size, cache.HeldChars);
    }

    [Fact]
    public void A_missing_file_fails_the_way_it_always_has()
    {
        var cache = new NoteContentCache();

        Assert.Throws<FileNotFoundException>(() => cache.Read(Path.Combine(_root, "gone.md")));
    }
}
