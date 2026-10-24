using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MdNotes.Services;
using MdNotes.Views.Dialogs;

namespace MdNotes.Views;

public sealed class DialogService : IDialogService
{
    // With several windows open, MainWindow may be the wrong one - or already closed - so the
    // dialog follows the window the user is actually looking at.
    private static Window Owner
    {
        get
        {
            var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            return desktop?.Windows.FirstOrDefault(w => w is MainWindow { IsActive: true })
                   ?? desktop?.Windows.OfType<MainWindow>().FirstOrDefault()
                   ?? desktop?.MainWindow
                   ?? throw new InvalidOperationException("No window is available to own the dialog.");
        }
    }

    public Task ShowErrorAsync(string message) => MessageDialog.ShowErrorAsync(Owner, message);

    public Task<bool> ConfirmAsync(string message) => MessageDialog.ConfirmAsync(Owner, message);

    public Task<string?> PromptTextAsync(string title, string initialValue) =>
        InputDialog.ShowAsync(Owner, title, initialValue);

    public async Task<string?> PickFolderAsync()
    {
        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a vault folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return null;

        return folders[0].TryGetLocalPath()
               ?? throw new InvalidOperationException($"The selected folder is not on the local file system: {folders[0].Name}");
    }
}
