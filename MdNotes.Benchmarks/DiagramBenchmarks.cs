using Avalonia.Svg.Skia;
using BenchmarkDotNet.Attributes;
using Mermaider;
using Mermaider.Models;
using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Benchmarks;

/// <summary>
/// Two findings meet on one string. Finding 10 says <see cref="SvgCssInliner.Inline"/> rebuilds the
/// whole SVG once per <c>var()</c>, which is quadratic in the document; finding 12 says every edit
/// inside a mermaid fence re-renders the diagram synchronously in the DiagramView constructor, and
/// asks for a real render to be timed before any asynchrony is added.
///
/// So both are timed on the same diagrams: the raw Mermaid render, the inlining on its own, the two
/// together, and the SvgSource load that follows them - which together are the whole synchronous
/// <c>DiagramView</c> constructor, through <c>DiagramCache.GetPicture</c>. The flowchart is the shape
/// the generated vault plants; the larger one is what a real note grows into.
/// </summary>
[MemoryDiagnoser]
public class DiagramBenchmarks
{
    private static readonly DiagramPalette Palette = new("#FFFFFF", "#222222");
    private static readonly MermaiderDiagramRenderer Renderer = new();

    private const string Small = """
        flowchart TD
          A[Underlag] --> B{Attest}
          B -->|Ja| C[Bokförs]
          B -->|Nej| D[Retur]
        """;

    private const string Large = """
        flowchart TD
          A[Underlag] --> B{Attest}
          B -->|Ja| C[Bokförs]
          B -->|Nej| D[Retur]
          C --> E[Momsrapport]
          C --> F[Huvudbok]
          E --> G{Avstämning}
          F --> G
          G -->|Diff| H[Utredning]
          G -->|Noll| I[Periodslut]
          H --> J[Rättelse]
          J --> F
          I --> K[Årsredovisning]
          K --> L[Revision]
          L -->|Anmärkning| H
          L -->|Ren| M[Fastställd]
        """;

    private string _smallRaw = "";
    private string _largeRaw = "";
    private string _largeInlined = "";

    /// <summary>The Mermaid output before inlining - what finding 10's loop is handed.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var options = new RenderOptions { Bg = Palette.Background, Fg = Palette.Foreground };
        _smallRaw = MermaidRenderer.RenderSvg(Small, options);
        _largeRaw = MermaidRenderer.RenderSvg(Large, options);
        _largeInlined = SvgCssInliner.Inline(_largeRaw);
        Console.WriteLine($"SVG sizes: small {_smallRaw.Length:N0} chars, large {_largeRaw.Length:N0} chars; " +
                          $"var() occurrences: {Occurrences(_smallRaw)} / {Occurrences(_largeRaw)}");
    }

    [Benchmark(Description = "Mermaid render only, 4-node flowchart")]
    public int RenderSmall() => MermaidRenderer.RenderSvg(Small, Options()).Length;

    [Benchmark(Description = "Mermaid render only, 13-node flowchart")]
    public int RenderLarge() => MermaidRenderer.RenderSvg(Large, Options()).Length;

    [Benchmark(Description = "Inline CSS only, 4-node flowchart")]
    public int InlineSmall() => SvgCssInliner.Inline(_smallRaw).Length;

    [Benchmark(Description = "Inline CSS only, 13-node flowchart")]
    public int InlineLarge() => SvgCssInliner.Inline(_largeRaw).Length;

    [Benchmark(Description = "SvgSource load only, 13-node flowchart")]
    public object? Load() => SvgSource.LoadFromSvg(_largeInlined).Picture;

    [Benchmark(Description = "Render + inline + load: the DiagramView constructor")]
    public object? Keystroke() => SvgSource.LoadFromSvg(Renderer.RenderSvg(Large, Palette)).Picture;

    private static RenderOptions Options() => new() { Bg = Palette.Background, Fg = Palette.Foreground };

    private static int Occurrences(string svg)
    {
        var count = 0;
        for (var i = svg.IndexOf("var(", StringComparison.Ordinal); i >= 0; i = svg.IndexOf("var(", i + 4, StringComparison.Ordinal))
            count++;
        return count;
    }
}
