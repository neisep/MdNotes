using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MdNotes.Services;

/// <summary>
/// Replaces CSS custom properties (<c>var(--x)</c>), <c>color-mix(in srgb, …)</c> and <c>rem</c> lengths in an
/// SVG document with literal values, so renderers without CSS support (Svg.Skia) draw it as intended.
/// </summary>
public static partial class SvgCssInliner
{
    private const double RemInPixels = 16;

    [GeneratedRegex("<svg[^>]*\\sstyle=\"([^\"]*)\"")]
    private static partial Regex RootStyleAttribute();

    [GeneratedRegex(@"\s*(?<![\w.#-])svg\s*\{[^}]*\}")]
    private static partial Regex SvgRule();

    public static string Inline(string svg)
    {
        var variables = new Dictionary<string, string>();

        if (RootStyleAttribute().Match(svg) is { Success: true } root)
            AddDeclarations(variables, root.Groups[1].Value);

        if (SvgRule().Match(svg) is { Success: true } rule)
        {
            var body = rule.Value[(rule.Value.IndexOf('{') + 1)..rule.Value.LastIndexOf('}')];
            AddDeclarations(variables, body);
            svg = svg.Remove(rule.Index, rule.Length);
        }

        // One pass, resuming after each replacement. Re-searching from the start and rebuilding the whole
        // document per var() was quadratic: Mermaid output has hundreds of them, and inlining a 15 KB SVG
        // allocated 5.3 MB. A resolved value never contains var(, so nothing it writes needs a second look.
        var resolver = new Resolver(variables);
        StringBuilder? builder = null;
        var at = 0;
        while (svg.IndexOf("var(", at, StringComparison.Ordinal) is var start and >= 0)
        {
            var end = MatchingParenthesis(svg, start + 3);
            builder ??= new StringBuilder(svg.Length);
            builder.Append(svg, at, start - at).Append(resolver.Evaluate(svg[start..(end + 1)]));
            at = end + 1;
        }
        return builder is null ? svg : builder.Append(svg, at, svg.Length - at).ToString();
    }

    private static void AddDeclarations(Dictionary<string, string> variables, string declarations)
    {
        foreach (var declaration in declarations.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0 || !declaration.StartsWith("--", StringComparison.Ordinal)) continue;
            variables[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
        }
    }

    private static int MatchingParenthesis(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        throw new FormatException($"Unbalanced parentheses in SVG at offset {open}.");
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(': depth++; break;
                case ')': depth--; break;
                case ',' when depth == 0:
                    parts.Add(text[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }
        parts.Add(text[start..].Trim());
        return parts;
    }

    private sealed class Resolver(Dictionary<string, string> variables)
    {
        public string Evaluate(string expression)
        {
            expression = expression.Trim();

            if (expression.StartsWith("var(", StringComparison.Ordinal))
            {
                var parts = SplitTopLevel(expression[4..^1]);
                if (variables.TryGetValue(parts[0], out var value)) return Evaluate(value);
                if (parts.Count > 1) return Evaluate(string.Join(',', parts.Skip(1)));
                throw new FormatException($"Undefined CSS variable {parts[0]}.");
            }

            if (expression.StartsWith("color-mix(", StringComparison.Ordinal))
                return Mix(expression);

            if (expression.EndsWith("rem", StringComparison.Ordinal))
            {
                var rem = double.Parse(expression[..^3], CultureInfo.InvariantCulture);
                return (rem * RemInPixels).ToString(CultureInfo.InvariantCulture);
            }

            return expression;
        }

        private string Mix(string expression)
        {
            var args = SplitTopLevel(expression[10..^1]);
            if (args.Count != 3 || !args[0].StartsWith("in srgb", StringComparison.Ordinal))
                throw new FormatException($"Unsupported color-mix(): {expression}");

            var (first, firstShare) = ColorArgument(args[1]);
            var (second, secondShare) = ColorArgument(args[2]);
            var p1 = firstShare ?? (secondShare is { } s ? 1 - s : 0.5);
            var p2 = secondShare ?? 1 - p1;
            var total = p1 + p2;

            var a = ParseHex(Evaluate(first));
            var b = ParseHex(Evaluate(second));
            return $"#{Channel(a.R, b.R)}{Channel(a.G, b.G)}{Channel(a.B, b.B)}";

            string Channel(int x, int y) => ((int)Math.Round((x * p1 + y * p2) / total)).ToString("X2");
        }

        private static (string Color, double? Share) ColorArgument(string argument)
        {
            var space = argument.LastIndexOf(' ');
            if (space < 0 || !argument.EndsWith('%')) return (argument, null);
            var percent = double.Parse(argument[(space + 1)..^1], CultureInfo.InvariantCulture);
            return (argument[..space], percent / 100);
        }

        private static (int R, int G, int B) ParseHex(string color)
        {
            if (color.Length is 4 or 5 && color[0] == '#') // #rgb / #rgba
                color = "#" + string.Concat(color[1..4].Select(c => new string(c, 2)));
            if (color.Length is not (7 or 9) || color[0] != '#')
                throw new FormatException($"Expected a hex color but found '{color}'.");
            return (Convert.ToInt32(color[1..3], 16), Convert.ToInt32(color[3..5], 16), Convert.ToInt32(color[5..7], 16));
        }
    }
}
