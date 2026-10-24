using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace MdNotes.Views.Markdown;

/// <summary>
/// The theme resources the preview paints with, looked up once per build and assigned directly. They
/// used to be a DynamicResource binding per paragraph, run, link, marker and cell - a live subscription
/// to the resource dictionaries each, thousands per 100 KB note, rebuilt every 250 ms while typing - and
/// that dynamism was never used, because <see cref="MarkdownView"/> rebuilds the whole tree when the
/// theme changes anyway. The same pattern as the diagram palette the view already resolves per build.
///
/// Each value is the resource itself, or <see cref="AvaloniaProperty.UnsetValue"/> when the key is not
/// found: exactly what the DynamicResource would have supplied, so assigning one changes nothing else.
/// </summary>
internal sealed record PreviewResources(
    object Text,
    object TextMuted,
    object Accent,
    object SurfaceCode,
    object Error,
    object Border,
    object FolderIcon,
    object CheckedIcon,
    object UncheckedIcon)
{
    public static PreviewResources From(Control host, ThemeVariant theme)
    {
        object Find(string key) =>
            host.TryFindResource(key, theme, out var value) && value is not null ? value : AvaloniaProperty.UnsetValue;

        return new PreviewResources(
            Find("MdText"),
            Find("MdTextMuted"),
            Find("MdAccent"),
            Find("MdSurfaceCode"),
            Find("MdError"),
            Find("MdBorder"),
            Find("Icon.folder"),
            Find("Icon.square-check"),
            Find("Icon.square"));
    }
}
