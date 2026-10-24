namespace MdNotes.Services;

/// <summary>
/// The text of notes already read. Vault-wide search reads every note; debounced at 250 ms that is
/// still one full read of the vault per word typed, and the read is the expensive part - folding and
/// matching then run over memory this already holds.
///
/// Keyed by path plus the file's write stamp, the freshness test <c>ImageCache</c> already uses here:
/// an edit from outside the app changes the stamp and the next read misses. What MdNotes writes itself
/// is handed straight to <see cref="Store"/>, so the app never has to trust a stamp for text it
/// produced - which is the case where being a tick stale would cost someone an edit.
///
/// Bounded by total characters rather than by entry count, because notes differ in size by orders of
/// magnitude and memory is what the bound is for. Evicting here is forgetting: strings are managed, so
/// unlike the bitmap caches there is nothing to dispose and nothing that can be disposed mid-use.
///
/// Recency is the list, not a stamp on the entry. A scan that overruns the budget evicts once per note
/// it reads, and picking the victim by searching the dictionary made that quadratic: measured over a
/// 5,000-note vault, a warm search cost 1,196 ms against 154 ms for a 2,000-note one - eight times the
/// work for two and a half times the notes. Moving to the front of a linked list makes each eviction
/// O(1), so the cost of overrunning is linear in what has to be re-read rather than in the square of it.
///
/// The budget is the other half of that: overrunning at all is what turns every scan back into a full
/// re-read of the vault, because a sequential pass over every note evicts in exactly the order it is
/// about to ask again. 48M characters is 96 MB of UTF-16, and at the ~2.7 KB average note this was
/// measured against it holds a vault of roughly eighteen thousand notes - nine thousand when every note
/// has an accent, because the cached fold is then a second copy. Past that the cache degrades to
/// re-reading rather than failing, and <see cref="HeldChars"/> against <see cref="Count"/> is how a
/// benchmark sees that it has.
/// </summary>
internal sealed class NoteContentCache(long budgetChars = 48_000_000)
{
    /// <summary>
    /// <see cref="Folded"/> is null until someone asks for it, and is the text itself when nothing in it
    /// folds - which is what makes it free for an all-ASCII note.
    /// </summary>
    private readonly record struct Entry(
        long Ticks, long Length, string Text, LinkedListNode<string> Node, string? Folded = null)
    {
        /// <summary>Characters this entry holds: the text, plus the fold when it is a copy of its own.</summary>
        public long Size => Text.Length + (Folded is null || ReferenceEquals(Folded, Text) ? 0 : Folded.Length);
    }

    // Locked, because a vault-wide scan now runs on the thread pool while the editor reads and writes
    // on the UI thread. The disk read deliberately happens outside the lock: it is the slow part, and
    // holding the lock across it would put the UI thread behind a background scan's OneDrive round trip
    // - which is the freeze the scan was moved off the UI thread to avoid. Two threads racing the same
    // uncached note therefore both read it, which costs one duplicated read of identical text.
    private readonly Lock _gate = new();

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths least recently read first, so the next victim is always the front of the list.</summary>
    private readonly LinkedList<string> _order = new();

    private long _held;
    private bool _overrun;

    public int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>Characters currently held, against the budget. Exposed so the bound can be asserted.</summary>
    public long HeldChars { get { lock (_gate) return _held; } }

    /// <summary>The bound itself, so a caller reporting on the cache does not have to restate the number.</summary>
    public long BudgetChars => budgetChars;

    /// <summary>
    /// Whether the budget has had to evict since the cache was last cleared - that is, whether the vault
    /// is larger than what this holds. Once it is, a pass over every note re-reads from disk rather than
    /// from here, and a caller that was relying on this to make a walk cheap can no longer do so.
    /// </summary>
    public bool HasOverrun { get { lock (_gate) return _overrun; } }

    /// <summary>
    /// The note's text, read from disk only when there is no current copy of it. A missing file falls
    /// through to the read so the caller gets the failure it has always got.
    /// </summary>
    public string Read(string fullPath)
    {
        var file = new FileInfo(fullPath);
        if (!file.Exists) return File.ReadAllText(fullPath);

        var ticks = file.LastWriteTimeUtc.Ticks;
        lock (_gate)
        {
            if (_entries.TryGetValue(fullPath, out var hit) && hit.Ticks == ticks && hit.Length == file.Length)
            {
                _order.Remove(hit.Node);
                _order.AddLast(hit.Node);
                return hit.Text;
            }
        }

        var text = File.ReadAllText(fullPath);
        lock (_gate) Put(fullPath, text, ticks, file.Length);
        return text;
    }

    /// <summary>
    /// The note's text and its <see cref="DiacriticFolder"/> fold. Folded once per read of the file rather
    /// than once per scan: the fold is a full copy of any note with an accent in it, which is nearly every
    /// Swedish note, and a scan used to make one of every note per keystroke. Held on the entry, dropped
    /// with it, and counted against the budget, because it is memory the same as the text is.
    ///
    /// The fold runs outside the lock for the reason the disk read does. Two threads folding the same
    /// note both fold it, and one of the identical answers is kept.
    /// </summary>
    public (string Text, string Folded) ReadFolded(string fullPath)
    {
        var text = Read(fullPath);
        lock (_gate)
        {
            if (_entries.TryGetValue(fullPath, out var hit) && ReferenceEquals(hit.Text, text) && hit.Folded is { } cached)
                return (text, cached);
        }

        var folded = DiacriticFolder.Fold(text);
        lock (_gate)
        {
            // Only onto the entry it was folded from: a save or an outside edit since then has replaced it,
            // and this fold belongs to text that is no longer there.
            if (_entries.TryGetValue(fullPath, out var entry) && ReferenceEquals(entry.Text, text) && entry.Folded is null)
            {
                var withFold = entry with { Folded = folded };
                _entries[fullPath] = withFold;
                _held += withFold.Size - entry.Size;
                Trim();
            }
        }
        return (text, folded);
    }

    /// <summary>
    /// Records text the app has just written. Takes the stamp from disk afterwards, so the entry is
    /// current by the same test a read uses rather than by being trusted.
    /// </summary>
    public void Store(string fullPath, string text)
    {
        var file = new FileInfo(fullPath);
        if (!file.Exists) return;

        lock (_gate) Put(fullPath, text, file.LastWriteTimeUtc.Ticks, file.Length);
    }

    /// <summary>Forgets everything. The vault changing root makes every path held here the wrong one.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
            _held = 0;
            _overrun = false;
        }
    }

    /// <summary>Callers hold <see cref="_gate"/>.</summary>
    private void Put(string fullPath, string text, long ticks, long length)
    {
        Forget(fullPath);

        _entries[fullPath] = new Entry(ticks, length, text, _order.AddLast(fullPath));
        _held += text.Length;
        Trim();
    }

    /// <summary>Callers hold <see cref="_gate"/>.</summary>
    private void Trim()
    {
        // One note larger than the whole budget would otherwise evict everything and then itself; it is
        // kept, because the caller is about to use it, and the next read of anything else trims it away.
        // It cannot be the victim below either way: it was just added or read, so it is at the far end.
        while (_held > budgetChars && _entries.Count > 1)
        {
            Forget(_order.First!.Value);
            _overrun = true;
        }
    }

    /// <summary>Callers hold <see cref="_gate"/>.</summary>
    private void Forget(string fullPath)
    {
        if (!_entries.Remove(fullPath, out var gone)) return;

        _order.Remove(gone.Node);
        _held -= gone.Size;
    }
}
