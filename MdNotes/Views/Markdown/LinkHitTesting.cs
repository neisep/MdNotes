using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MdNotes.Views.Markdown;

/// <summary>
/// Avalonia has no clickable Hyperlink inline, so links are plain styled text and clicks are mapped back
/// to character ranges through the TextBlock's text layout. This keeps links wrapping like ordinary text.
/// </summary>
internal static class LinkHitTesting
{
    private static readonly Cursor Hand = new(StandardCursorType.Hand);

    public static void Attach(TextBlock text, IReadOnlyList<LinkRange> links, Action<string> open)
    {
        text.Background = Brushes.Transparent; // makes the whole block hit-testable
        text.PointerMoved += (_, e) => text.Cursor = Hit(text, links, e.GetPosition(text)) is null ? Cursor.Default : Hand;
        text.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && Hit(text, links, e.GetPosition(text)) is { } link)
                open(link.Url);
        };
    }

    private static LinkRange? Hit(TextBlock text, IReadOnlyList<LinkRange> links, Point point)
    {
        var hit = text.TextLayout.HitTestPoint(point);
        if (!hit.IsInside) return null;
        var index = hit.TextPosition;
        return links.FirstOrDefault(l => index >= l.Start && index < l.End);
    }
}
