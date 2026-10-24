using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

public class LruTests
{
    private sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void The_evicted_value_is_disposed()
    {
        var lru = new Lru<int, Probe>(1);
        var first = lru.GetOrAdd(1, () => new Probe());

        lru.GetOrAdd(2, () => new Probe());

        Assert.True(first.Disposed);
        Assert.Equal(1, lru.Count);
    }

    // A cache dropped whole used to take everything still in it with it, unreleased - which for the
    // diagram pictures is the unmanaged Skia memory the cache exists to bound.
    [Fact]
    public void Dispose_releases_everything_still_held()
    {
        var lru = new Lru<int, Probe>(4);
        var held = new[] { lru.GetOrAdd(1, () => new Probe()), lru.GetOrAdd(2, () => new Probe()) };

        lru.Dispose();

        Assert.All(held, probe => Assert.True(probe.Disposed));
        Assert.Equal(0, lru.Count);
    }
}
