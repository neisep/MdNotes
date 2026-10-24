using Avalonia.Controls;
using Avalonia;

namespace MdNotes.Views.Dialogs;

public partial class MessageDialog : Window
{
    public MessageDialog() => InitializeComponent();

    public static Task ShowErrorAsync(Window owner, string message) =>
        Create("MdNotes", message, ("OK", true, IsDefault: true, IsCancel: true)).ShowDialog(owner);

    /// <summary>Returns true for Yes; No and closing the window both return false.</summary>
    public static Task<bool> ConfirmAsync(Window owner, string message) =>
        Create("Confirm", message, ("Yes", true, IsDefault: true, IsCancel: false), ("No", false, IsDefault: false, IsCancel: true))
            .ShowDialog<bool>(owner);

    private static MessageDialog Create(
        string title, string message, params (string Text, bool Result, bool IsDefault, bool IsCancel)[] buttons)
    {
        var dialog = new MessageDialog();
        dialog.TitleBlock.Text = title;
        dialog.Message.Text = message;
        var isFirstButton = true;
        foreach (var (text, result, isDefault, isCancel) in buttons)
        {
            var button = new Button { Content = text, MinWidth = 88, CornerRadius = new(6), Padding = new(12, 6), IsDefault = isDefault, IsCancel = isCancel };
            if (isFirstButton && isDefault)
            {
                button.Classes.Add("primary");
                isFirstButton = false;
            }
            button.Click += (_, _) => dialog.Close(result);
            dialog.Buttons.Children.Add(button);
        }
        return dialog;
    }
}
