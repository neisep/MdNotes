using Avalonia;
using Avalonia.Media;
using MdNotes.Services;

namespace MdNotes;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Hand the request to a running MdNotes before building any UI, so a double-click in
        // Explorer costs a window rather than a second copy of the app.
        var instance = new SingleInstance("MdNotes");
        if (instance.TryHandOff(StartupArgs.FilePath(args))) return;

        App.Instance = instance;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            instance.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://MdNotes/Assets/Fonts#Inter" });
}
