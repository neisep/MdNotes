using MdNotes.Services;

namespace MdNotes.ViewModels;

/// <summary>
/// Turns a failure a command could see coming into a message the user can act on - a name already
/// taken, a note that has gone, a folder outside the vault. The exception's own message is what gets
/// shown, because whoever threw it wrote it for the user.
///
/// Not the only way an exception becomes a dialog, and it should not pretend to be: whatever escapes a
/// guard reaches the crash barrier in <c>App</c>, which says it was unexpected and names its type.
/// This one is for the failures a command expects; that one is for the bugs it does not. If a message
/// arrives looking like the second, a guard is missing rather than the failure being unusual.
///
/// Extension methods rather than a class of its own because there is no state to hold: the thing that
/// has to be shared between the window and its workspace is the <see cref="IDialogService"/>, and they
/// already share that.
/// </summary>
internal static class DialogGuard
{
    /// <summary>
    /// The synchronous path. The dialog is deliberately not awaited: the caller is a command handler or
    /// a property setter that cannot block, and the message is the whole of what it has to say.
    /// </summary>
    public static void Guard(this IDialogService dialogs, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _ = dialogs.ShowErrorAsync(ex.Message);
        }
    }

    public static async Task GuardAsync(this IDialogService dialogs, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await dialogs.ShowErrorAsync(ex.Message);
        }
    }

    /// <summary>
    /// For the two callers that answer a question - "did the vault open", "may the window close".
    /// False already means "not this time" to both of them, for reasons that are not errors at all,
    /// such as the user cancelling the picker; a failure is simply one more of those.
    /// </summary>
    public static async Task<bool> TryAsync(this IDialogService dialogs, Func<Task<bool>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await dialogs.ShowErrorAsync(ex.Message);
            return false;
        }
    }
}
