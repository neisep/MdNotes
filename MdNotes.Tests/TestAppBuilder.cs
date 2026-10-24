using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using MdNotes.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// Avalonia's Dispatcher.UIThread is one per process, and the headless session builds an application
// around it per test class. Run two classes at once and the second sets its application up while the
// first still owns the dispatcher, which fails that test's setup rather than the test. Harmless while
// every path was synchronous - a test could not outlive its own last line; not harmless now that the
// search and the tree walk finish on the thread pool.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MdNotes.Tests;

/// <summary>Runs the real App (theme + preview resources) headlessly, with Skia so frames can be rendered.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .With(new FontManagerOptions { DefaultFamilyName = "avares://MdNotes/Assets/Fonts#Inter" });
}
