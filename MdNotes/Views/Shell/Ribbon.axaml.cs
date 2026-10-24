using Avalonia.Controls;

namespace MdNotes.Views.Shell;

public partial class Ribbon : UserControl
{
    public event Action? SearchButtonClicked;

    public Ribbon()
    {
        InitializeComponent();
        SearchButton.Click += (_, _) => SearchButtonClicked?.Invoke();
    }
}
