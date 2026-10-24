using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MdNotes.ViewModels;

namespace MdNotes.Views.Shell;

/// <summary>
/// The switcher's own keys. The scrim, the card, Escape and focus-on-show belong to
/// <see cref="ModalOverlay"/>, which hosts this.
/// </summary>
public partial class QuickSwitcher : UserControl
{
    public QuickSwitcher()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private QuickSwitcherViewModel Vm => (QuickSwitcherViewModel)DataContext!;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Vm.MoveSelectionCommand.Execute(1);
                e.Handled = true;
                break;

            case Key.Up:
                Vm.MoveSelectionCommand.Execute(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Vm.OpenSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnResultsDoubleTapped(object? sender, RoutedEventArgs e) =>
        Vm.OpenSelectedCommand.Execute(null);
}
