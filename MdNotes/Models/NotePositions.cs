namespace MdNotes.Models;

/// <summary>
/// Where the caret was in each note, so coming back to one lands where you left rather than at the top.
///
/// Bounded and least-recently-used, because this outlives the session: a vault visited for years would
/// otherwise grow an entry per note in the settings file and never drop one. Ordered by an explicit
/// counter rather than by insertion, because a Dictionary reuses the slot a removal freed - re-recording
/// a note would not move it to the newest end, which is the whole point of the ordering.
///
/// Keyed by full path, which is also what makes a deleted note fall out: the key stops being asked for
/// and the bound eventually forgets it.
///
/// Offsets are recorded as given and clamped when read, because the note may have been edited elsewhere
/// since - a stored offset is a hint about a file that has moved on, not a promise.
/// </summary>
public sealed class NotePositions(int capacity = 200)
{
    private readonly Dictionary<string, (int Caret, long LastUsed)> _offsets =
        new(StringComparer.OrdinalIgnoreCase);

    private long _clock;

    public int Count => _offsets.Count;

    /// <summary>Every remembered position, oldest first - the order they are persisted and reloaded in.</summary>
    public IEnumerable<KeyValuePair<string, int>> All =>
        _offsets.OrderBy(e => e.Value.LastUsed)
            .Select(e => new KeyValuePair<string, int>(e.Key, e.Value.Caret));

    /// <summary>
    /// Records where the caret was. Re-recording a note makes it the newest, so the notes being worked
    /// in are the last ones forgotten.
    /// </summary>
    public void Remember(string fullPath, int caret)
    {
        if (string.IsNullOrEmpty(fullPath) || caret < 0) return;

        _offsets[fullPath] = (caret, ++_clock);

        while (_offsets.Count > capacity)
            _offsets.Remove(_offsets.MinBy(e => e.Value.LastUsed).Key);
    }

    /// <summary>
    /// Where to put the caret in a note of <paramref name="length"/> characters: what was remembered,
    /// clamped to the text that is actually there now, or 0 for a note never visited.
    /// </summary>
    public int Restore(string? fullPath, int length) =>
        fullPath is not null && _offsets.TryGetValue(fullPath, out var hit)
            ? Math.Clamp(hit.Caret, 0, Math.Max(0, length))
            : 0;

    public void Forget(string fullPath) => _offsets.Remove(fullPath);
}
