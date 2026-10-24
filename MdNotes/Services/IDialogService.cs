namespace MdNotes.Services;

public interface IDialogService
{
    Task ShowErrorAsync(string message);
    Task<bool> ConfirmAsync(string message);
    Task<string?> PromptTextAsync(string title, string initialValue);
    Task<string?> PickFolderAsync();
}
