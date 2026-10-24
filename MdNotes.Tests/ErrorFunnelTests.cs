using MdNotes.ViewModels;

namespace MdNotes.Tests;

/// <summary>
/// The two paths an exception can take to a dialog, and that they do not sound alike. The guard reports
/// a failure the command expected, in the words it was thrown with; the crash barrier reports one that
/// escaped a guard, and says so. Telling them apart is the feedback F10 found missing - a command with
/// no guard used to produce a dialog indistinguishable from a handled failure.
/// </summary>
public class ErrorFunnelTests
{
    [Fact]
    public void A_guarded_failure_is_shown_in_the_words_it_was_thrown_with()
    {
        var dialogs = new FakeDialogs();

        dialogs.Guard(() => throw new InvalidOperationException("A note named 'Ideas' already exists."));

        Assert.Equal("A note named 'Ideas' already exists.", Assert.Single(dialogs.Errors));
    }

    [Fact]
    public void The_crash_barrier_says_the_failure_was_unexpected_and_names_its_type()
    {
        var message = App.CrashMessage(new InvalidOperationException("Collection was modified."));

        Assert.Contains("did not expect", message);
        Assert.Contains(nameof(InvalidOperationException), message);
        Assert.Contains("Collection was modified.", message);
    }

    /// <summary>
    /// The point of the split: a handled failure must not arrive wearing the crash barrier's words, or
    /// the barrier stops being a signal that something is missing a guard.
    /// </summary>
    [Fact]
    public void A_guarded_failure_does_not_sound_like_a_crash()
    {
        var dialogs = new FakeDialogs();

        dialogs.Guard(() => throw new InvalidOperationException("A note named 'Ideas' already exists."));

        Assert.DoesNotContain("did not expect", Assert.Single(dialogs.Errors));
    }
}
