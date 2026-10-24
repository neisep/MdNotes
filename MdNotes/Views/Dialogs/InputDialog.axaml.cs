using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MdNotes.Views.Dialogs;

public partial class InputDialog : Window
{
    public InputDialog() => InitializeComponent();

    /// <summary>Shows the prompt; returns the entered text, or null when cancelled.</summary>
    public static Task<string?> ShowAsync(Window owner, string title, string initialValue)
    {
        var dialog = new InputDialog();
        dialog.TitleBlock.Text = title;
        dialog.Input.Text = initialValue;
        dialog.Opened += (_, _) =>
        {
            dialog.Input.Focus();
            dialog.Input.SelectAll();
        };
        return dialog.ShowDialog<string?>(owner);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Input.Text);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
