namespace MdNotes.Models;

/// <summary>
/// The colours a diagram is drawn in, as the literal strings the renderer needs. The app's palette is
/// a XAML resource dictionary and the renderer is in the services layer, so the colours are resolved
/// once at render time and travelled down rather than restated here - a second palette is how diagrams
/// came to keep their own shade of grey while the rest of the app moved.
/// </summary>
/// <param name="Background">Behind the diagram; the surface it sits on, so there is no seam.</param>
/// <param name="Foreground">Diagram text and the marks that carry it.</param>
public readonly record struct DiagramPalette(string Background, string Foreground);
