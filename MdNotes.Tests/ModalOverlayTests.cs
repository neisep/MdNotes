using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using MdNotes.Services;
using MdNotes.ViewModels;
using MdNotes.Views;
using MdNotes.Views.Markdown;
using MdNotes.Views.Shell;

namespace MdNotes.Tests;

/// <summary>
/// The chrome the three overlays used to carry a copy of each. These run against the real window,
/// because what is being tested is the wiring between the overlay and its content.
/// </summary>
public sealed class ModalOverlayTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly FakeDialogs _dialogs = new();
    private MainViewModel? _vm;

    public void Dispose()
    {
        _vm?.Dispose();
        _vault.Dispose();
    }

    private async Task<(Window Window, MainViewModel Vm)> ShellAsync()
    {
        _vault.Note("Todo.md", "- [ ] ship the widget");
        _vm = new MainViewModel(TestServices.For(_vault, _dialogs));
        var window = new MainWindow { DataContext = _vm, Width = 1400, Height = 850 };
        window.Show();
        Assert.True(await _vm.TryOpenInitialVaultAsync());
        return (window, _vm);
    }

    private static ModalOverlay Overlay<TViewModel>(Window window) =>
        window.GetVisualDescendants().OfType<ModalOverlay>().Single(o => o.DataContext is TViewModel);

    [AvaloniaFact]
    public async Task All_three_overlays_share_one_chrome()
    {
        var (window, _) = await ShellAsync();

        var overlays = window.GetVisualDescendants().OfType<ModalOverlay>().ToList();
        Assert.Equal(3, overlays.Count);
        Assert.All(overlays, o => Assert.False(o.IsVisible));
    }

    [AvaloniaFact]
    public async Task Opening_focuses_the_field_the_content_marks()
    {
        var (window, vm) = await ShellAsync();

        vm.Workspace.OpenQuickSwitcherCommand.Execute(null);
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        var input = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Input");
        Assert.True(input.IsFocused);
    }

    [AvaloniaFact]
    public async Task Escape_closes_every_overlay()
    {
        var (window, vm) = await ShellAsync();

        foreach (var (open, isOpen) in new (Action, Func<bool>)[]
                 {
                     (() => vm.Workspace.OpenQuickSwitcherCommand.Execute(null), () => vm.Workspace.QuickSwitcher.IsOpen),
                     (() => vm.OpenSettingsCommand.Execute(null), () => vm.Settings.IsOpen),
                 })
        {
            open();
            window.CaptureRenderedFrame();
            Assert.True(isOpen());

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(isOpen());
        }
    }

    [AvaloniaFact]
    public async Task Closing_from_outside_commits_a_field_that_writes_on_lost_focus()
    {
        var (window, vm) = await ShellAsync();
        vm.OpenSettingsCommand.Execute(null);
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        var overlay = Overlay<SettingsViewModel>(window);
        var excludeBox = overlay.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ExcludeBox");
        excludeBox.Focus();
        excludeBox.Text = "build/";

        // Escape is the quickest way out, and it used to throw the typed list away in every overlay
        // but this one. The fix now lives in the chrome, so it cannot be had by only one of them.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(vm.Settings.IsOpen);
        Assert.Equal("build/", vm.Settings.ExcludedPatterns);
        Assert.Empty(_dialogs.Errors);
    }
}
