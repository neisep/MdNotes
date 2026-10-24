using Mermaider;
using Mermaider.Models;
using MdNotes.Models;

namespace MdNotes.Services;

public sealed class MermaiderDiagramRenderer : IDiagramRenderer
{
    public string RenderSvg(string mermaidSource, DiagramPalette palette)
    {
        // The caller resolved these from the theme, so a diagram cannot drift from the page around it.
        var options = new RenderOptions
        {
            Bg = palette.Background,
            Fg = palette.Foreground,
        };
        // Mermaider styles through CSS variables; the SVG control needs literal colours.
        return SvgCssInliner.Inline(MermaidRenderer.RenderSvg(mermaidSource, options));
    }
}
