using Avalonia.Controls;

namespace MdNotes.Views.Shell;

/// <summary>
/// Markup only. The scrim, the card, Escape and focus-on-show belong to <see cref="ModalOverlay"/>,
/// which also takes focus off the text boxes before closing so their LostFocus bindings commit -
/// a fix this panel used to carry alone.
/// </summary>
public partial class SettingsPanel : UserControl
{
    public SettingsPanel() => InitializeComponent();
}
