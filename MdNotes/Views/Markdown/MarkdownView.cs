using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media;
using Markdig.Syntax;
using MdNotes.Models;
using MdNotes.Services;
using MdNotes.Services.Markdown;

namespace MdNotes.Views.Markdown;

/// <summary>
/// Renders a Markdig document as native controls. Wikilinks (https://note.local/…) run
/// <see cref="LinkCommand"/> with the note name; other web links open in the default browser.
/// </summary>
public sealed class MarkdownView : Decorator, IDisposable
{
    public static readonly StyledProperty<MarkdownDocument?> DocumentProperty =
        AvaloniaProperty.Register<MarkdownView, MarkdownDocument?>(nameof(Document));

    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<MarkdownView, ICommand?>(nameof(LinkCommand));

    public static readonly StyledProperty<string?> BaseDirectoryProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(BaseDirectory));

    public static readonly StyledProperty<string?> ContentRootProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(ContentRoot));

    public static readonly StyledProperty<IDiagramRenderer?> DiagramRendererProperty =
        AvaloniaProperty.Register<MarkdownView, IDiagramRenderer?>(nameof(DiagramRenderer));

    private readonly ImageCache _images = new();
    private readonly DiagramSlots _slots = new();
    private DiagramCache? _diagrams;
    private bool _rebuildQueued;

    /// <summary>A rebuild was asked for while the view was hidden, and is owed when it is shown.</summary>
    private bool _stale;

    static MarkdownView()
    {
        DocumentProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.QueueRebuild());
        BaseDirectoryProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.QueueRebuild());
        ContentRootProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.QueueRebuild());
        DiagramRendererProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnRendererChanged());
    }

    public MarkdownView() => ActualThemeVariantChanged += (_, _) => QueueRebuild();

    /// <summary>
    /// Its own <see cref="Visual.IsVisible"/>, not effective visibility: Avalonia keeps the change
    /// notification for that internal, so the window binds this view's visibility as well as its
    /// container's, and this is the one that is watched.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible && _stale) QueueRebuild();
    }

    /// <summary>Rebuilds that actually ran. Exposed so a test can tell a skipped build from a done one.</summary>
    internal int Rebuilds { get; private set; }

    public MarkdownDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    /// <summary>
    /// Renders the Mermaid fences. A service, handed in like every other dependency a view takes -
    /// it used to be a static written by <c>App</c>, which every test that built a window had to
    /// write too. The picture and bitmap caches are built on it and belong to this view.
    /// </summary>
    public IDiagramRenderer? DiagramRenderer
    {
        get => GetValue(DiagramRendererProperty);
        set => SetValue(DiagramRendererProperty, value);
    }

    /// <summary>Folder that relative image paths resolve against.</summary>
    public string? BaseDirectory
    {
        get => GetValue(BaseDirectoryProperty);
        set => SetValue(BaseDirectoryProperty, value);
    }

    /// <summary>
    /// Folder an image must sit under once resolved - the vault for a note inside one. Switching vaults
    /// changes what is reachable, so a change here rebuilds the preview. Null contains images to
    /// <see cref="BaseDirectory"/>, which is the closed default rather than "anywhere on disk".
    /// </summary>
    public string? ContentRoot
    {
        get => GetValue(ContentRootProperty);
        set => SetValue(ContentRootProperty, value);
    }

    // Opening a note changes BaseDirectory and Document back to back; one rebuild per dispatcher pass is enough.
    //
    // Hidden - Edit-only mode hides the preview, see OnPropertyChanged - nothing is built at all: a 100 KB note
    // measured at 56 ms and 19 MB per rebuild, every 250 ms of typing, for a tree nobody could see. The
    // rebuild is owed instead and paid once, against whatever the document is by then, on being shown.
    private void QueueRebuild()
    {
        if (!IsVisible)
        {
            _stale = true;
            return;
        }

        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            // Hidden between the request and now: still owed, still not worth building.
            if (!IsVisible)
            {
                _stale = true;
                return;
            }
            Rebuild();
        }, DispatcherPriority.Render);
    }

    private void Rebuild()
    {
        _stale = false;
        Rebuilds++;

        // Retire the last build's images before this one asks for any: what it carries over survives,
        // what it does not is disposed one build later, when nothing on screen can still hold it.
        _images.BeginDocument();
        _images.DecodeWidth = ImageCache.DecodeWidthFor(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        _slots.BeginDocument();

        if (Document is not { } document)
        {
            Child = null;
            return;
        }

        Child = BlockRenderer.RenderDocument(document,
            new RenderContext(Diagrams(), _images, BaseDirectory, ContentRoot, Palette(), OpenLink, _slots, OnDiagramLanded,
                PreviewResources.From(this, ActualThemeVariant)));
    }

    /// <summary>
    /// A diagram rendered off the UI thread has landed. Shown by rebuilding, which now finds it in the
    /// cache - but only if the latest build is still waiting for it: one typed past is let go.
    /// </summary>
    private bool OnDiagramLanded(string source, DiagramPalette palette)
    {
        if (!_slots.IsAwaited(source, palette)) return false;
        QueueRebuild();
        return true;
    }

    /// <summary>
    /// The one point where the app's palette reaches diagram rendering. The renderer needs literal
    /// colour strings, so they are resolved here from the same brushes the rest of the preview uses -
    /// which is what keeps a diagram from sitting on a background the page does not have.
    /// </summary>
    private DiagramPalette Palette()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        return new DiagramPalette(
            Resolve("MdSurface", dark ? "#1E1E1E" : "#FFFFFF"),
            Resolve("MdText", dark ? "#DADADA" : "#222222"));
    }

    /// <summary>The brush as '#RRGGBB'; the fallback keeps a diagram rendering if a key ever moves.</summary>
    private string Resolve(string key, string fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : fallback;

    /// <summary>The diagram cache this view has built, if any. For tests that look at what it holds.</summary>
    internal DiagramCache? DiagramCacheInUse => _diagrams;

    private DiagramCache Diagrams()
    {
        var renderer = DiagramRenderer
                       ?? throw new InvalidOperationException("MarkdownView.DiagramRenderer has not been set.");
        return _diagrams ??= new DiagramCache(renderer);
    }

    // The caches are built on one renderer, so a new one invalidates them - and the old pictures are
    // unmanaged Skia memory, which is why they are disposed rather than dropped.
    private void OnRendererChanged()
    {
        _diagrams?.Dispose();
        _diagrams = null;
        QueueRebuild();
    }

    /// <summary>
    /// Releases the caches this view owns. Called when the window closes, not when the control
    /// detaches: a control can detach and reattach, and closing is the event that means "done".
    /// </summary>
    public void Dispose()
    {
        _diagrams?.Dispose();
        _diagrams = null;
        _images.Dispose();
    }

    private void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

        if (WikiLinkExtension.TargetFromUrl(uri) is { Length: > 0 } target)
        {
            if (LinkCommand?.CanExecute(target) == true) LinkCommand.Execute(target);
        }
        else if (uri.Scheme is "http" or "https" or "mailto")
        {
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
        }
    }
}
