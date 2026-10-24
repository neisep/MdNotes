using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>Finding 15: a watcher burst schedules one check, and the check waits for the burst to end.</summary>
public class BurstGateTests
{
    private long _now;
    private BurstGate Gate() => new(TimeSpan.FromMilliseconds(300), () => _now);

    [Fact]
    public void Only_the_first_signal_of_a_burst_asks_for_a_check()
    {
        var gate = Gate();

        Assert.True(gate.Signal());
        for (var i = 0; i < 1000; i++) Assert.False(gate.Signal());
    }

    /// <summary>The check settles only once the burst has been quiet: after the last signal, not the first.</summary>
    [Fact]
    public void The_check_waits_for_the_last_signal_to_go_quiet()
    {
        var gate = Gate();
        gate.Signal();
        _now = 250;
        gate.Signal();

        _now = 300;
        Assert.False(gate.TrySettle(out var remaining)); // 50 ms since the last signal
        Assert.Equal(TimeSpan.FromMilliseconds(250), remaining);
        _now = 550;
        Assert.True(gate.TrySettle(out _));
    }

    [Fact]
    public void A_settled_gate_starts_a_new_burst_on_the_next_signal()
    {
        var gate = Gate();
        gate.Signal();
        _now = 300;
        Assert.True(gate.TrySettle(out _));

        Assert.True(gate.Signal());
    }

    [Fact]
    public void Signals_from_many_threads_ask_for_exactly_one_check()
    {
        var gate = new BurstGate(TimeSpan.FromMilliseconds(300));
        var asked = 0;

        Parallel.For(0, 10_000, _ => { if (gate.Signal()) Interlocked.Increment(ref asked); });

        Assert.Equal(1, asked);
    }
}
