using Avalonia.Controls;

namespace MdNotes.Views.Shell;

/// <summary>
/// Markup only. The scrim, the card, Escape and focus-on-show belong to <see cref="ModalOverlay"/>,
/// which hosts this; Enter deliberately does nothing, because a bulk rewrite takes a deliberate click.
/// </summary>
public partial class SearchReplace : UserControl
{
    public SearchReplace() => InitializeComponent();
}
