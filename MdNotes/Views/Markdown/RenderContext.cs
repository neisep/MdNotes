using MdNotes.Models;

namespace MdNotes.Views.Markdown;

/// <summary>Everything the block and inline renderers need besides the syntax tree itself.</summary>
/// <param name="BaseDirectory">Folder a relative image URL resolves against: the note's own folder.</param>
/// <param name="ContentRoot">Folder an image must sit under once resolved; null contains it to <paramref name="BaseDirectory"/>.</param>
/// <param name="Slots">What each diagram showed last build, so an edited one keeps showing it meanwhile.</param>
/// <param name="DiagramLanded">Told when a background render lands; true when it is still wanted.</param>
/// <param name="Look">The theme's brushes and icons, resolved once for this build.</param>
internal sealed record RenderContext(
    DiagramCache Diagrams,
    ImageCache Images,
    string? BaseDirectory,
    string? ContentRoot,
    DiagramPalette Diagram,
    Action<string> OpenLink,
    DiagramSlots Slots,
    Func<string, DiagramPalette, bool> DiagramLanded,
    PreviewResources Look);

/// <summary>A clickable character range [Start, End) inside one paragraph's text.</summary>
internal sealed record LinkRange(int Start, int End, string Url);
