using MdNotes.Services;

namespace MdNotes.Tests;

public class SvgCssInlinerTests
{
    private const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" style="--bg:#FFFFFF;--fg:#000000">
        <style>
          text { font-family: sans-serif; }
          svg {
            --_half: color-mix(in srgb, var(--fg) 50%, var(--bg));
            --_tenth: var(--surface, color-mix(in srgb, var(--fg) 10%, var(--bg)));
            --fs-s: 0.875rem;
          }
        </style>
        <rect fill="var(--_half)" stroke="var(--_tenth)" />
        <text font-size="var(--fs-s)" fill="var(--accent, var(--fg))">x</text>
        </svg>
        """;

    [Fact]
    public void Resolves_variables_mixes_colours_and_converts_rem()
    {
        var result = SvgCssInliner.Inline(Svg);

        Assert.Contains("fill=\"#808080\"", result);
        Assert.Contains("stroke=\"#E6E6E6\"", result);
        Assert.Contains("font-size=\"14\"", result);
        Assert.Contains("fill=\"#000000\"", result);
        Assert.DoesNotContain("var(", result);
        Assert.DoesNotContain("--_half", result);
        Assert.Contains("text { font-family: sans-serif; }", result);
    }

    /// <summary>
    /// The single-pass splice (finding 10): text between, before and after every var() is kept exactly,
    /// and a fallback that is itself a var() still resolves.
    /// </summary>
    [Fact]
    public void Every_var_is_replaced_in_place_and_the_text_around_them_is_kept()
    {
        var result = SvgCssInliner.Inline(
            "<svg style=\"--a:#111111;--b:2rem\"><g x=\"var(--a)\" y=\"var(--b)\">var(--a)</g>" +
            "<g fill=\"var(--none, var(--a))\"/>tail</svg>");

        Assert.Equal(
            "<svg style=\"--a:#111111;--b:2rem\"><g x=\"#111111\" y=\"32\">#111111</g>" +
            "<g fill=\"#111111\"/>tail</svg>",
            result);
    }

    [Fact]
    public void An_svg_with_nothing_to_inline_is_returned_as_it_is()
    {
        const string plain = "<svg><rect fill=\"#000\"/></svg>";

        Assert.Same(plain, SvgCssInliner.Inline(plain));
    }

    [Fact]
    public void Undefined_variable_without_fallback_throws()
    {
        Assert.Throws<FormatException>(() => SvgCssInliner.Inline("<svg><rect fill=\"var(--missing)\"/></svg>"));
    }
}
