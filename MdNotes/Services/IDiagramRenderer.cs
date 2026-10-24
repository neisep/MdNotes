using MdNotes.Models;

namespace MdNotes.Services;

public interface IDiagramRenderer
{
    /// <summary>Renders Mermaid source to a self-contained SVG document in the given colours.
    /// Throws on invalid diagram source.</summary>
    string RenderSvg(string mermaidSource, DiagramPalette palette);
}
