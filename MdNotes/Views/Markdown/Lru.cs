namespace MdNotes.Views.Markdown;

/// <summary>
/// Bounded least-recently-used map; the evicted value is disposed, and so is everything still held
/// when the map itself is dropped - a cache thrown away whole used to leak all of it.
/// </summary>
internal sealed class Lru<TKey, TValue>(int capacity) : IDisposable where TKey : notnull where TValue : IDisposable
{
    private readonly Dictionary<TKey, (TValue Value, long LastUsed)> _entries = [];
    private long _clock;

    public int Count => _entries.Count;

    /// <summary>Whether <paramref name="key"/> is held, without counting as a use of it.</summary>
    public bool Contains(TKey key) => _entries.ContainsKey(key);

    public TValue GetOrAdd(TKey key, Func<TValue> create)
    {
        if (_entries.TryGetValue(key, out var hit))
        {
            _entries[key] = (hit.Value, ++_clock);
            return hit.Value;
        }

        var value = create();
        if (_entries.Count >= capacity)
        {
            var oldest = _entries.MinBy(e => e.Value.LastUsed);
            oldest.Value.Value.Dispose();
            _entries.Remove(oldest.Key);
        }
        _entries[key] = (value, ++_clock);
        return value;
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Value.Dispose();
        _entries.Clear();
    }
}
