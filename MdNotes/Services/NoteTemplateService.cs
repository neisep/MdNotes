using MdNotes.Models;

namespace MdNotes.Services;

/// <summary>
/// The one global template, named by <see cref="ISettingsService.NewNoteTemplatePath"/> and read through
/// the vault. Every failure - unconfigured, no vault, outside the vault, missing, unreadable - resolves
/// to "" so the note is simply created blank, which is what it did before this feature existed.
/// <paramref name="folderPath"/> is unused today; it is what per-folder templates would resolve against,
/// so adding them stays a change inside this class.
/// </summary>
public sealed class NoteTemplateService(VaultService vault, ISettingsService settings) : INoteTemplateService
{
    public string ContentFor(string folderPath, string name)
    {
        if (ResolveTemplatePath() is not { } template) return string.Empty;
        if (IsTheTemplateItself(template, folderPath, name)) return string.Empty;
        if (TryRead(template) is not { } text) return string.Empty;

        return NoteTemplate.Apply(text, Path.GetFileNameWithoutExtension(name), DateTimeOffset.Now);
    }

    public TemplateState Probe()
    {
        if (ResolveTemplatePath() is not { } template) return TemplateState.None;
        if (TryRead(template) is not null) return TemplateState.Ready;

        // Something is there but will not read - a folder of that name, a lock, no permission.
        return File.Exists(template) || Directory.Exists(template)
            ? TemplateState.Unreadable
            : TemplateState.Missing;
    }

    /// <summary>
    /// The configured template as a full path inside the vault, or null. GetFullPath runs before the
    /// containment check because PathScope compares prefixes: un-normalised, "root/../../x" starts with
    /// the root and would pass.
    /// </summary>
    private string? ResolveTemplatePath()
    {
        if (vault.RootPath is not { } root) return null;
        if (NoteTemplate.NormalizePath(settings.NewNoteTemplatePath) is not { } relative) return null;

        var full = Path.GetFullPath(Path.Combine(root, relative));
        return PathScope.IsSameOrUnder(full, root) ? full : null;
    }

    /// <summary>
    /// Creating the template file itself must not seed it with its own expansion - the natural order is
    /// to name the path in settings and only then create the note it points at.
    /// </summary>
    private static bool IsTheTemplateItself(string template, string folderPath, string name) =>
        string.Equals(Path.GetDirectoryName(template), Path.GetFullPath(folderPath), StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetFileNameWithoutExtension(template), Path.GetFileNameWithoutExtension(name),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Null when the template cannot be read - missing, locked, denied, or a folder.</summary>
    private string? TryRead(string path)
    {
        try
        {
            return vault.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
