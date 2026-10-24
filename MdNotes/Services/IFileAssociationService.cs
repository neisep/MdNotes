namespace MdNotes.Services;

/// <summary>Makes MdNotes the program the OS opens .md files with.</summary>
public interface IFileAssociationService
{
    /// <summary>False where MdNotes cannot register itself; the offer is then hidden.</summary>
    bool IsSupported { get; }

    /// <summary>True once the OS actually opens .md files with this copy of MdNotes.</summary>
    bool IsDefault { get; }

    /// <summary>
    /// Registers MdNotes as a Markdown handler for the current user and asks the OS to let the
    /// user confirm it as the default. Windows will not hand over the default silently.
    /// </summary>
    void Register();
}
