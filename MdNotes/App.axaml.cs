using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.ViewModels;
using MdNotes.Views;

namespace MdNotes;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Without a desktop lifetime (e.g. headless UI tests) only the theme and resources are set up.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            StartDesktop(desktop);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Set by <see cref="Program"/> before Avalonia starts; null under the headless tests.</summary>
    internal static SingleInstance? Instance { get; set; }

    private static void StartDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var dialogs = new DialogService();

        // The crash barrier, and deliberately not the funnel DialogGuard is: that one reports a failure
        // the user caused and can do something about, this one reports a bug that reached the dispatcher.
        // Handled rather than fatal because a notes app that exits takes the buffer someone was typing
        // into with it - but it says the failure was unexpected, which is what keeps a command missing
        // its guard visible while the app is being built instead of indistinguishable from a handled one.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = true;
            _ = dialogs.ShowErrorAsync(CrashMessage(args.Exception));
        };

        var session = new AppSession(desktop, dialogs);

        if (Instance is { } instance)
        {
            // A later launch hands its file here instead of starting a second MdNotes.
            instance.FileRequested += path => Dispatcher.UIThread.Post(() =>
                session.NewWindow(string.IsNullOrEmpty(path) ? null : path));
            instance.Listen();
        }

        session.NewWindow(StartupArgs.FilePath(desktop.Args ?? []));
    }

    /// <summary>
    /// What the crash barrier shows. It names the exception type and says the app may not be sound,
    /// because unlike a guarded failure nobody wrote this text for the user - it is a bug surfacing,
    /// and the honest thing to say is that saving and restarting is the safe move.
    /// </summary>
    internal static string CrashMessage(Exception exception) =>
        "Something went wrong that MdNotes did not expect. It has carried on so you do not lose the "
        + "note you are editing, but it may no longer be in a reliable state - save your work and "
        + $"restart when you can.\n\n{exception.GetType().Name}: {exception.Message}";

    internal static void ApplyTheme(ThemeMode mode) =>
        Application.Current!.RequestedThemeVariant = mode switch
        {
            ThemeMode.System => ThemeVariant.Default,
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
}
