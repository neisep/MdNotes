using System.Globalization;
using System.Text.RegularExpressions;

namespace MdNotes.Models;

/// <summary>
/// What a new-note template is: the placeholders it may contain, and what a usable path to one looks
/// like. Pure text - reading the file and deciding which one to read belongs to the service.
/// </summary>
public static partial class NoteTemplate
{
    public const string Extension = ".md";
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    /// <summary>
    /// Fills {{title}}, {{date}} and {{time}}, each of which may carry a ":format". Anything else - an
    /// unknown name, a format the framework rejects - is left exactly as written, because a template is
    /// someone's text and blanking part of it is worse than leaving a placeholder showing.
    /// One pass: chained Replace would re-scan its own output and substitute twice in a note whose
    /// title happens to contain "{{date}}".
    /// </summary>
    public static string Apply(string template, string title, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        return Placeholder().Replace(template, match =>
        {
            var format = match.Groups[2].Success ? match.Groups[2].Value : null;
            return match.Groups[1].Value.ToLowerInvariant() switch
            {
                // A format argument means nothing to a title, so "{{title:x}}" is left alone.
                "title" when format is null => title,
                "date" => Format(now, format ?? DateFormat, match.Value),
                "time" => Format(now, format ?? TimeFormat, match.Value),
                _ => match.Value,
            };
        });
    }

    /// <summary>Invariant culture, so a template reads the same on every machine and the tests are stable.</summary>
    private static string Format(DateTimeOffset now, string format, string original)
    {
        try
        {
            return now.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return original;
        }
    }

    /// <summary>
    /// The setting as it can be used: trimmed, '/'-separated, '.md' implied, null when there is nothing
    /// to use. Null too for anything that would leave the vault - settings.json is hand-editable, so
    /// this is the boundary check, and a value that cannot be used means "no template", never a throw.
    /// </summary>
    public static string? NormalizePath(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return null;

        var path = setting.Trim().Replace('\\', '/').TrimStart('/');
        if (path.Length == 0) return null;

        // Rooted paths and '..' segments both name something the vault does not own.
        if (Path.IsPathRooted(path) || path.Contains(':')) return null;
        if (path.Split('/').Any(segment => segment is ".." or ".")) return null;
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;

        return path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z]+)\s*(?::([^}]*))?\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();
}

/// <summary>Whether the configured template can actually be used, for the settings panel to report.</summary>
public enum TemplateState
{
    /// <summary>No template configured, or no vault to resolve it against: new notes start blank.</summary>
    None,
    Ready,
    Missing,
    Unreadable,
}
