using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MdNotes.Views.Shell;

/// <summary>
/// The chrome every modal overlay shares: the scrim, the card, Escape, click-outside and
/// focus-on-show. The overlays own their content and nothing else - three copies of this is how the
/// settings panel ended up with a focus fix the other two never got.
/// </summary>
public sealed class ModalOverlay : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ModalOverlay, bool>(nameof(IsOpen));

    /// <summary>Run when Escape or the scrim asks to close. The view models do more than hide
    /// themselves, so closing goes through them rather than through <see cref="IsOpen"/>.</summary>
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ModalOverlay, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<ModalOverlay, double>(nameof(CardWidth), double.NaN);

    public static readonly StyledProperty<double> CardMaxHeightProperty =
        AvaloniaProperty.Register<ModalOverlay, double>(nameof(CardMaxHeight), double.PositiveInfinity);

    public static readonly StyledProperty<Thickness> CardMarginProperty =
        AvaloniaProperty.Register<ModalOverlay, Thickness>(nameof(CardMargin));

    public static readonly StyledProperty<Thickness> CardPaddingProperty =
        AvaloniaProperty.Register<ModalOverlay, Thickness>(nameof(CardPadding));

    /// <summary>
    /// Marks the control to focus when the overlay opens. Attached rather than inferred from markup
    /// order, because the settings panel docks its Done button first and reordering it would move it.
    /// </summary>
    public static readonly AttachedProperty<bool> InitialFocusProperty =
        AvaloniaProperty.RegisterAttached<ModalOverlay, Control, bool>("InitialFocus");

    public static void SetInitialFocus(Control control, bool value) => control.SetValue(InitialFocusProperty, value);
    public static bool GetInitialFocus(Control control) => control.GetValue(InitialFocusProperty);

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    public double CardWidth { get => GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }
    public double CardMaxHeight { get => GetValue(CardMaxHeightProperty); set => SetValue(CardMaxHeightProperty, value); }
    public Thickness CardMargin { get => GetValue(CardMarginProperty); set => SetValue(CardMarginProperty, value); }
    public Thickness CardPadding { get => GetValue(CardPaddingProperty); set => SetValue(CardPaddingProperty, value); }

    public ModalOverlay()
    {
        IsVisible = false;
        // Focusable so that closing can take focus off a text box first; not a tab stop, because it is
        // chrome and nothing in it is worth tabbing to.
        Focusable = true;
        IsTabStop = false;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Border>("PART_Scrim") is { } scrim)
            scrim.PointerPressed += (_, _) => RequestClose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsOpenProperty) return;

        IsVisible = IsOpen;
        if (IsOpen) Dispatcher.UIThread.Post(FocusFirstField);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        RequestClose();
        e.Handled = true;
    }

    /// <summary>
    /// Text boxes that commit on LostFocus get one before the overlay goes away, so a list someone has
    /// just typed is not thrown out by the quickest way of closing the panel.
    /// </summary>
    private void RequestClose()
    {
        Focus();
        if (CloseCommand?.CanExecute(null) == true) CloseCommand.Execute(null);
    }

    private void FocusFirstField()
    {
        if (!IsOpen) return;

        var target = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(GetInitialFocus)
                     ?? this.GetVisualDescendants().OfType<InputElement>()
                         .FirstOrDefault(c => c is not ModalOverlay && c.Focusable && c.IsEffectivelyVisible) as Control;

        target?.Focus();
        if (target is TextBox box) box.SelectAll();
    }
}
