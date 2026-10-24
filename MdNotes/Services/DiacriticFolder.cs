using System.Globalization;
using System.Text;

namespace MdNotes.Services;

/// <summary>
/// Strips diacritics for comparison, so "resume" matches "résumé" and "jonkoping" matches "Jönköping".
/// The fold is length-preserving - one char in, one char out - so an index into the folded string is
/// still valid in the original. Letters with no single-char base (æ, ß) are left alone, and so is text
/// already in decomposed form (NFD), where the accent is a separate char that cannot be dropped.
/// </summary>
public static class DiacriticFolder
{
    private const int TableSize = 0x300; // Latin-1 Supplement through Latin Extended-B

    private static readonly char[] Table = BuildTable();

    /// <summary>The unaccented form of <paramref name="text"/>, or <paramref name="text"/> if nothing folds.</summary>
    public static string Fold(string text)
    {
        var first = 0;
        while (first < text.Length && Fold(text[first]) == text[first]) first++;
        if (first == text.Length) return text;

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = Fold(source[i]);
        });
    }

    private static char Fold(char c) =>
        c < TableSize ? Table[c] : FoldUncached(c);

    private static char[] BuildTable()
    {
        var table = new char[TableSize];
        for (var c = 0; c < TableSize; c++)
            table[c] = FoldUncached((char)c);

        // Letters whose accent is a stroke or bar, which Unicode does not decompose.
        table['ø'] = 'o'; table['Ø'] = 'O';
        table['đ'] = 'd'; table['Đ'] = 'D';
        table['ð'] = 'd'; table['Ð'] = 'D';
        table['ł'] = 'l'; table['Ł'] = 'L';
        table['ħ'] = 'h'; table['Ħ'] = 'H';
        table['ŧ'] = 't'; table['Ŧ'] = 'T';
        table['ı'] = 'i';
        return table;
    }

    private static char FoldUncached(char c)
    {
        // Half of a surrogate pair is not a string Normalize can take, and an emoji in a note must
        // not throw; it has no diacritic to strip either way.
        if (c < 0x80 || char.IsSurrogate(c)) return c;

        var baseChar = c.ToString().Normalize(NormalizationForm.FormD)[0];
        return CharUnicodeInfo.GetUnicodeCategory(baseChar) == UnicodeCategory.NonSpacingMark ? c : baseChar;
    }
}
