using MdNotes.Models;

namespace MdNotes.Views.Markdown;

/// <summary>
/// What each Mermaid block showed in the last build, by position in the note, so an edited diagram can
/// keep its previous picture on screen while the new one renders off the UI thread - instead of
/// blinking out on every keystroke typed inside the fence. Position is the only identity a block has;
/// inserting a diagram above another shows the wrong previous picture for one render, which is the
/// price of not inventing one.
/// </summary>
internal sealed class DiagramSlots
{
    private List<(string Source, DiagramPalette Palette)> _previous = [];
    private List<(string Source, DiagramPalette Palette)> _current = [];
    private HashSet<(string Source, DiagramPalette Palette)> _awaited = [];

    /// <summary>Starts a build: this build's slots become the ones the next build looks back at.</summary>
    public void BeginDocument()
    {
        _previous = _current;
        _current = [];
        _awaited = [];
    }

    /// <summary>What the block about to be rendered showed last build, or null if it did not exist then.</summary>
    public (string Source, DiagramPalette Palette)? Previous =>
        _current.Count < _previous.Count ? _previous[_current.Count] : null;

    /// <summary>Records what this block shows now. Called once per block, in document order.</summary>
    public void Shown(string source, DiagramPalette palette) => _current.Add((source, palette));

    /// <summary>Records that this build is waiting for a diagram rendering in the background.</summary>
    public void Await(string source, DiagramPalette palette) => _awaited.Add((source, palette));

    /// <summary>Whether the latest build is still waiting for this diagram - a render it typed past is not.</summary>
    public bool IsAwaited(string source, DiagramPalette palette) => _awaited.Contains((source, palette));
}
