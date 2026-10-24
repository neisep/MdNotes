using Avalonia.Controls;
using MdNotes.Views.Shell;
using MdNotes.ViewModels;

namespace MdNotes.Views;

public partial class MainWindow : Window
{
    private double _sidebarWidth = 260;
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        // Subscribe to search button once (handler uses Vm property to get current VM)
        Ribbon.SearchButtonClicked += () =>
        {
            if (!Vm.IsSidebarVisible)
            {
                Vm.IsSidebarVisible = true;
            }
            Sidebar.FocusSearchBox();
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from previous VM
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        if (DataContext is not MainViewModel vm) return;

        _vm = vm;

        // Subscribe to PropertyChanged once
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.IsSidebarVisible))
        {
            OnSidebarVisibilityChanged(_vm!.IsSidebarVisible);
        }
    }

    private void OnSidebarVisibilityChanged(bool isVisible)
    {
        var sidebarCol = Shell.ColumnDefinitions[1];

        if (isVisible)
        {
            // Show sidebar: restore or use minimum width
            var width = _sidebarWidth > 0 ? _sidebarWidth : 260;
            sidebarCol.Width = new GridLength(Math.Max(180, width));
            Sidebar.IsVisible = true;
        }
        else
        {
            // Hide sidebar: save width and collapse
            if (sidebarCol.Width.IsAbsolute)
            {
                _sidebarWidth = sidebarCol.Width.Value;
            }
            sidebarCol.Width = new GridLength(0);
            Sidebar.IsVisible = false;
        }
    }

    // Leaving the app counts as leaving the note: the gate is a no-op unless the mode saves implicitly.
    // One of our own dialogs taking focus is not leaving, and must not save again: a note that cannot
    // be written would otherwise answer its own error dialog with another one.
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (OwnedWindows.Count > 0) return;
        if (DataContext is MainViewModel vm) vm.SaveImplicitly();
    }

    // Closing may have to ask about an unsaved loose file, and Closing cannot await: cancel the
    // close, ask, then close again for real.
    private bool _closeConfirmed;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;

        e.Cancel = true;
        if (!await Vm.TryCloseAsync()) return;

        _closeConfirmed = true;
        Vm.Dispose();
        // The preview owns the diagram and image caches - unmanaged Skia memory a second window
        // must not keep paying for.
        Preview.Dispose();
        Close();
    }
}
