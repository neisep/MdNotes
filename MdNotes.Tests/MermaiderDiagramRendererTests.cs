using MdNotes.Models;
using MdNotes.Services;

namespace MdNotes.Tests;

public class MermaiderDiagramRendererTests
{
    private readonly MermaiderDiagramRenderer _renderer = new();
    private static readonly DiagramPalette Light = new("#FFFFFF", "#222222");
    private static readonly DiagramPalette Dark = new("#1E1E1E", "#DADADA");

    [Fact]
    public void Flowchart_renders_to_svg_with_labels_and_literal_colours()
    {
        var svg = _renderer.RenderSvg("flowchart LR\n  A[Start] --> B{Decision}\n  B -->|Yes| C[Done]", Light);

        Assert.Contains("<svg", svg);
        Assert.Contains("Start", svg);
        Assert.Contains("Decision", svg);
        Assert.DoesNotContain("var(", svg);
        Assert.DoesNotContain("color-mix(", svg);
        Assert.DoesNotContain("rem", svg);
    }

    [Fact]
    public void The_caller_chooses_the_colours()
    {
        var svg = _renderer.RenderSvg("sequenceDiagram\n  Alice->>Bob: Hello\n  Bob-->>Alice: Hi", Dark);

        Assert.Contains("<svg", svg);
        Assert.Contains("Alice", svg);
        // The colours come from the palette the caller resolved, not from a default inside the renderer.
        Assert.Contains($"fill=\"{Dark.Foreground}\"", svg);
        Assert.DoesNotContain("#DDDDDD", svg);
    }

    [Fact]
    public void Unknown_diagram_type_throws()
    {
        Assert.ThrowsAny<Exception>(() => _renderer.RenderSvg("notADiagram\n  foo --> bar", Light));
    }
}
