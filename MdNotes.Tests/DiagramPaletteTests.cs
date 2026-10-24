using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.Views.Markdown;

namespace MdNotes.Tests;

/// <summary>
/// Diagrams used to carry their own two colours, so editing Themes/Palette.axaml restyled the whole
/// app except them. These pin the colours a diagram is rendered in to the brushes everything else uses.
/// </summary>
public class DiagramPaletteTests
{
    /// <summary>Records the palettes it was asked to render in, and renders nothing.</summary>
    private sealed class RecordingRenderer : IDiagramRenderer
    {
        public List<DiagramPalette> Palettes { get; } = [];

        public string RenderSvg(string mermaidSource, DiagramPalette palette)
        {
            Palettes.Add(palette);
            // Enough of an SVG for the cache to load; the colours are what is under test.
            return """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10"/></svg>""";
        }
    }

    private static string Brush(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryFindResource(key, variant, out var value));
        var colour = Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        return $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void A_diagram_is_drawn_in_the_same_surface_and_text_colours_as_the_page(string themeName)
    {
        var variant = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var renderer = new RecordingRenderer();
        var before = Application.Current!.RequestedThemeVariant;

        try
        {
            Application.Current.RequestedThemeVariant = variant;
            var view = new MarkdownView
            {
                DiagramRenderer = renderer,
                Document = new MarkdownRenderer().Parse("```mermaid\nflowchart LR\n A --> B\n```"),
            };
            var window = new Window { Content = view, Width = 600, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var used = Assert.Single(renderer.Palettes.Distinct());
            Assert.Equal(Brush("MdSurface", variant), used.Background);
            Assert.Equal(Brush("MdText", variant), used.Foreground);
            window.Close();
        }
        finally
        {
            Application.Current.RequestedThemeVariant = before;
        }
    }
}
