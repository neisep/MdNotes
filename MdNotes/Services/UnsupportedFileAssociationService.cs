namespace MdNotes.Services;

/// <summary>Stands in where registering a handler is not implemented (everything but Windows so far).</summary>
public sealed class UnsupportedFileAssociationService : IFileAssociationService
{
    public bool IsSupported => false;

    public bool IsDefault => false;

    public void Register() => throw new PlatformNotSupportedException(
        "MdNotes cannot register itself as the Markdown handler on this platform.");
}
