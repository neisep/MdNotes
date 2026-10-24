using System.Diagnostics;
using MdNotes.Services;

namespace MdNotes.Tests;

/// <summary>
/// Finding 16: a launch with nothing running must not wait out the pipe's connect timeout, and the
/// hand-off to a running instance must still work. Each test uses its own app name, so a real MdNotes
/// running on the machine is never the instance found.
/// </summary>
public class SingleInstanceTests
{
    private static string UniqueApp() => "MdNotesTest" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public void With_nothing_running_a_launch_does_not_wait_for_the_pipe()
    {
        using var instance = new SingleInstance(UniqueApp());

        var clock = Stopwatch.StartNew();
        Assert.False(instance.TryHandOff("note.md"));

        Assert.True(clock.ElapsedMilliseconds < 150, $"took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task A_second_launch_hands_its_file_to_the_running_instance()
    {
        var app = UniqueApp();
        using var running = new SingleInstance(app);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        running.FileRequested += path => received.TrySetResult(path);
        running.Listen();

        using var second = new SingleInstance(app);
        Assert.True(second.TryHandOff("/notes/plan.md"));

        Assert.Equal("/notes/plan.md", await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Once_the_running_instance_closes_a_launch_starts_its_own_again()
    {
        var app = UniqueApp();
        var running = new SingleInstance(app);
        running.Listen();
        running.Dispose();

        using var next = new SingleInstance(app);
        Assert.False(next.TryHandOff(null));
    }
}
