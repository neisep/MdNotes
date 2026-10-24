using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using BenchmarkDotNet.Attributes;
using Markdig.Syntax;
using MdNotes.Services;
using MdNotes.Tests;
using MdNotes.Views.Markdown;

namespace MdNotes.Benchmarks;

/// <summary>
/// Findings 7 and 8 are both about <c>MarkdownView.Rebuild</c>: finding 7 that it runs every 250 ms
/// even when the preview is hidden, finding 8 that every control it makes takes DynamicResource
/// subscriptions the view never uses. Both need the same number first - what one rebuild of a 100 KB
/// note costs - so that is what this measures, next to the Markdig parse that precedes it.
///
/// No mermaid fences in the sample: a synchronous diagram render would dominate the rebuild and is
/// measured on its own in <see cref="DiagramBenchmarks"/>. This is the control tree, nothing else.
/// </summary>
[MemoryDiagnoser]
public class RebuildBenchmarks
{
    private readonly MarkdownRenderer _parser = new();

    private string _markdown = "";
    private MarkdownDocument _a = null!;
    private MarkdownDocument _b = null!;
    private MarkdownView _view = null!;
    private bool _toggle;

    [Params(10, 100)]
    public int Kb { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _markdown = Sample(Kb);
        _a = _parser.Parse(_markdown);
        _b = _parser.Parse(_markdown);
        Console.WriteLine($"Sample note: {_markdown.Length:N0} chars.");

        // A platform, not a window: Rebuild builds controls and never renders a frame.
        AppBuilder.Configure<MdNotes.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .With(new FontManagerOptions { DefaultFamilyName = "avares://MdNotes/Assets/Fonts#Inter" })
            .SetupWithoutStarting();

        _view = new MarkdownView { DiagramRenderer = new MermaiderDiagramRenderer() };
        // Attached, so the theme brushes the renderers ask for actually resolve.
        new Window { Content = _view };
        Flush();
    }

    [Benchmark(Description = "Markdig parse")]
    public MarkdownDocument Parse() => _parser.Parse(_markdown);

    [Benchmark(Description = "Rebuild the control tree")]
    public object? Rebuild()
    {
        // Two equal documents alternating: setting the same reference back raises no change and would
        // rebuild nothing, so each iteration is exactly one rebuild.
        _toggle = !_toggle;
        _view.Document = _toggle ? _a : _b;
        Flush();
        return _view.Child;
    }

    /// <summary>Rebuild is posted at Render priority; running the queue is what makes it happen.</summary>
    private static void Flush() => Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

    /// <summary>
    /// A note of the size the review names, built from the generated vault's own filler so it holds
    /// the mix a real note does - prose, wikilinks, tables and code fences. Index 7 is skipped: that
    /// is the mermaid one.
    /// </summary>
    private static string Sample(int kb)
    {
        var random = new Random(1);
        var parts = new List<string>();
        var chars = 0;
        for (var i = 0; chars < kb * 1024; i++)
        {
            var index = i % 11;
            if (index == 7) continue;
            var part = VaultShape.Note(index, kb: 2, random);
            parts.Add(part);
            chars += part.Length;
        }
        return string.Join("\n\n", parts);
    }
}
