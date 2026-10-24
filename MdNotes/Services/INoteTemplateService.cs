using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// Decides what a new note starts from. Separate from <see cref="VaultService"/> on purpose: the vault
/// writes whatever bytes it is handed, and which bytes those are is policy.
/// </summary>
public interface INoteTemplateService
{
    /// <summary>
    /// The text a new note called <paramref name="name"/> in <paramref name="folderPath"/> starts from,
    /// or "" when no template applies. Never throws: a template problem must not stop a note being made.
    /// </summary>
    string ContentFor(string folderPath, string name);

    /// <summary>Whether the configured template can be read right now. For the settings panel.</summary>
    TemplateState Probe();
}
