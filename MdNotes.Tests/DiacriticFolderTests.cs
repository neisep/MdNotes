using MdNotes.Services;

namespace MdNotes.Tests;

public sealed class DiacriticFolderTests
{
    private const string Ascii = "Meeting notes 2026-09-17";

    [Theory]
    [InlineData("résumé", "resume")]
    [InlineData("Jönköping", "Jonkoping")]
    [InlineData("Ångström", "Angstrom")]
    [InlineData("Malmø", "Malmo")]
    [InlineData("Łódź", "Lodz")]
    [InlineData("Çağrı", "Cagri")]
    [InlineData("naïve café", "naive cafe")]
    public void Accents_are_stripped(string input, string expected) =>
        Assert.Equal(expected, DiacriticFolder.Fold(input));

    [Fact]
    public void Case_is_preserved() =>
        Assert.Equal("OoAa", DiacriticFolder.Fold("ÖoÅä"));

    [Theory]
    [InlineData("résumé")]
    [InlineData("Jönköping")]
    [InlineData("Encyclopædia")]
    [InlineData("")]
    public void Fold_preserves_length(string input) =>
        Assert.Equal(input.Length, DiacriticFolder.Fold(input).Length);

    [Fact]
    public void Ascii_is_returned_without_allocating() =>
        Assert.Same(Ascii, DiacriticFolder.Fold(Ascii));

    [Theory]
    [InlineData("Encyclopædia")]
    [InlineData("Straße")]
    [InlineData("cœur")]
    public void Letters_without_a_single_char_base_are_left_alone(string input) =>
        Assert.Equal(input, DiacriticFolder.Fold(input));

    [Fact]
    public void Surrogate_pairs_pass_through_instead_of_throwing() =>
        Assert.Equal("cafe 🎉", DiacriticFolder.Fold("café 🎉"));
}
