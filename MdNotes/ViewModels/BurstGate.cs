namespace MdNotes.ViewModels;

/// <summary>
/// Turns a burst of signals from any thread into one scheduled check, and that check into one action
/// once the burst has gone quiet. A build or a git checkout inside the vault is thousands of watcher
/// events; posting a closure to the UI thread for each, only to restart the same timer, queued
/// thousands of dispatcher items. Now the first signal of a burst schedules the check and the rest
/// only record when they happened.
/// </summary>
/// <param name="quiet">How long without a signal counts as the burst being over.</param>
/// <param name="clock">Milliseconds, monotonic. A parameter so a test can move time by hand.</param>
internal sealed class BurstGate(TimeSpan quiet, Func<long> clock)
{
    private int _armed;
    private long _lastSignal;

    public BurstGate(TimeSpan quiet) : this(quiet, () => Environment.TickCount64) { }

    /// <summary>
    /// Records a signal. True for the first one since the gate last settled: the caller schedules a
    /// check. Every other signal returns false and needs nothing scheduled - the pending check will see it.
    /// </summary>
    public bool Signal()
    {
        Volatile.Write(ref _lastSignal, clock());
        return Interlocked.Exchange(ref _armed, 1) == 0;
    }

    /// <summary>
    /// The scheduled check. True when the burst is over: the gate reopens and the caller acts. False
    /// while a signal landed within the quiet period, with <paramref name="remaining"/> the rest of that
    /// period - checking again then acts exactly one quiet period after the last signal, which is when
    /// restarting a timer per signal used to. A signal arriving after this says true is a new burst, or
    /// is already covered, because the caller acts after it.
    /// </summary>
    public bool TrySettle(out TimeSpan remaining)
    {
        var since = clock() - Volatile.Read(ref _lastSignal);
        if (since < quiet.TotalMilliseconds)
        {
            remaining = quiet - TimeSpan.FromMilliseconds(since);
            return false;
        }

        remaining = TimeSpan.Zero;
        Volatile.Write(ref _armed, 0);
        return true;
    }
}
