using System.Globalization;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Host.Tests;

/// <summary>
/// wwwroot/css/tokens.css: every theme a person can pick defines every colour token (a missing one would silently fall
/// back to the light theme's), and every theme × accent pair keeps WCAG AA contrast — 4.5:1 for text (body, secondary,
/// muted, links, status words, text on the accent and danger buttons) and 3:1 for control boundaries and the focus ring.
/// Colours are computed from the OKLCH values in the file, with the theme's own and each preference accent.
/// </summary>
public sealed partial class ThemeTokensTests
{
    private static readonly string[] Tokens =
    [
        "--surface-0", "--surface-1", "--surface-2", "--surface-3", "--surface-nav", "--overlay", "--shadow-color",
        "--border", "--border-strong", "--text", "--text-2", "--text-3", "--text-on-accent",
        "--accent-h", "--accent-c", "--accent-c-hi", "--accent-solid", "--accent-solid-hover", "--accent-text", "--accent-tint",
        "--success", "--success-tint", "--warning", "--warning-tint", "--danger", "--danger-tint", "--danger-solid", "--text-on-danger", "--info", "--info-tint",
        "--chart-1", "--chart-2", "--chart-3", "--chart-4", "--chart-5", "--chart-6", "--chart-7", "--chart-8",
        "--status-ok", "--status-warn", "--status-down", "--status-off"
    ];

    private static readonly Css Sheet = Css.Parse(File.ReadAllText(Repo.Path("src", "Skanyxx.Host", "wwwroot", "css", "tokens.css")));

    public static TheoryData<string> Themes() => [.. PreferenceCatalog.Themes];

    public static TheoryData<string, string> Pairs() =>
        [.. from t in PreferenceCatalog.Themes from a in PreferenceCatalog.Accents select (t, a)];

    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryTheme_DefinesEveryToken(string theme)
    {
        Assert.NotEmpty(Sheet.Theme(theme));
        foreach (var block in Sheet.Theme(theme))
        {
            var missing = Tokens.Where(t => !block.ContainsKey(t)).ToList();
            Assert.True(missing.Count == 0, $"theme {theme} misses {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void EveryAccent_HasItsBlock_AndTheSystemThemeHasADarkVariant()
    {
        foreach (var accent in PreferenceCatalog.Accents.Where(a => a != "auto"))
            Assert.All(new[] { "--accent-h", "--accent-c", "--accent-c-hi" }, t => Assert.True(Sheet.Accent(accent).ContainsKey(t), $"accent {accent} misses {t}"));
        Assert.Equal(2, Sheet.Theme("system").Count);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryThemeAndAccent_MeetsWcagAA(string theme, string accent)
    {
        Assert.NotEmpty(Sheet.Theme(theme));
        foreach (var block in Sheet.Theme(theme))
        {
            var tokens = new Dictionary<string, string>(block);
            if (accent != "auto")
                foreach (var (k, v) in Sheet.Accent(accent))
                    tokens[k] = v;
            var c = (string name) => Color.Parse(Resolve(tokens, $"var({name})"));
            var problems = new List<string>();
            void Need(string fg, string bg, double min, Rgb? bgColor = null)
            {
                var back = bgColor ?? c(bg).Over(Rgb.White);
                var ratio = Rgb.Contrast(c(fg).Over(back), back);
                if (ratio < min) problems.Add($"{fg} on {bg}: {ratio:F2} < {min}");
            }

            foreach (var bg in new[] { "--surface-0", "--surface-1", "--surface-2", "--surface-3", "--surface-nav" })
                foreach (var fg in new[] { "--text", "--text-2", "--text-3", "--accent-text" })
                    if (!(bg == "--surface-3" && fg == "--accent-text"))
                        Need(fg, bg, 4.5);
            Need("--accent-text", "--accent-tint", 4.5, c("--accent-tint").Over(c("--surface-nav").Over(Rgb.White)));
            Need("--accent-text", "--accent-tint", 4.5, c("--accent-tint").Over(c("--surface-1").Over(Rgb.White)));
            Need("--text-on-accent", "--accent-solid", 4.5);
            Need("--text-on-accent", "--accent-solid-hover", 4.5);
            Need("--border-strong", "--surface-0", 3);
            Need("--border-strong", "--surface-1", 3);
            Need("--border-strong", "--surface-2", 3);
            Need("--accent-text", "--surface-0", 3); // focus ring
            foreach (var status in new[] { "success", "warning", "danger", "info" })
            {
                Need($"--{status}", "--surface-0", 4.5);
                Need($"--{status}", "--surface-1", 4.5);
                Need($"--{status}", $"--{status}-tint", 4.5, c($"--{status}-tint").Over(c("--surface-1").Over(Rgb.White)));
            }
            Need("--text-on-danger", "--danger-solid", 4.5);

            Assert.True(problems.Count == 0, $"{theme} + {accent}: {string.Join("; ", problems)}");
        }
    }

    private static string Resolve(IReadOnlyDictionary<string, string> tokens, string value, int depth = 0)
    {
        Assert.True(depth < 10, $"var() loop in {value}");
        return Var().Replace(value, m => Resolve(tokens, tokens.TryGetValue(m.Groups[1].Value, out var v) ? v : throw new KeyNotFoundException(m.Groups[1].Value), depth + 1));
    }

    [GeneratedRegex(@"var\((--[a-z0-9-]+)\)")]
    private static partial Regex Var();

    /// <summary>The rule blocks of the token sheet, with whether each sits inside an @media rule.</summary>
    private sealed partial class Css(List<(string Selector, bool InMedia, Dictionary<string, string> Declarations)> blocks)
    {
        public static Css Parse(string text)
        {
            text = Comment().Replace(text, "");
            var blocks = new List<(string, bool, Dictionary<string, string>)>();
            foreach (Match m in Block().Matches(text))
            {
                var depth = text[..m.Index].Count(ch => ch == '{') - text[..m.Index].Count(ch => ch == '}');
                var declarations = m.Groups[2].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(d => d.StartsWith("--", StringComparison.Ordinal))
                    .Select(d => d.Split(':', 2))
                    .ToDictionary(d => d[0].Trim(), d => d[1].Trim());
                blocks.Add((m.Groups[1].Value.Trim(), depth > 0, declarations));
            }
            return new Css(blocks);
        }

        /// <summary>The theme's block(s): one, or for "system" its light block and its dark @media block.</summary>
        public List<Dictionary<string, string>> Theme(string name) =>
            [.. blocks.Where(b => b.Selector.Split(',').Select(s => s.Trim()).Contains($"[data-theme=\"{name}\"]") && b.Declarations.ContainsKey("--surface-0"))
                .Select(b => b.Declarations)];

        public Dictionary<string, string> Accent(string name) =>
            blocks.Single(b => b.Selector == $"[data-accent=\"{name}\"]").Declarations;

        [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
        private static partial Regex Comment();

        [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}")]
        private static partial Regex Block();
    }

    private readonly record struct Color(Rgb Rgb, double Alpha)
    {
        public Rgb Over(Rgb back) => Rgb.Mix(Rgb, back, Alpha);

        public static Color Parse(string value)
        {
            var m = Regex.Match(value, @"^oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*(?:/\s*([\d.]+)\s*)?\)$");
            Assert.True(m.Success, $"not an oklch() colour: {value}");
            double N(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            return new(Rgb.FromOklch(N(1), N(2), N(3)), m.Groups[4].Success ? N(4) : 1);
        }
    }

    /// <summary>Gamma-encoded sRGB, 0–1.</summary>
    private readonly record struct Rgb(double R, double G, double B)
    {
        public static readonly Rgb White = new(1, 1, 1);

        public static Rgb FromOklch(double l, double c, double hDegrees)
        {
            var h = hDegrees * Math.PI / 180;
            var (a, b) = (c * Math.Cos(h), c * Math.Sin(h));
            var l_ = Math.Pow(l + 0.3963377774 * a + 0.2158037573 * b, 3);
            var m_ = Math.Pow(l - 0.1055613458 * a - 0.0638541728 * b, 3);
            var s_ = Math.Pow(l - 0.0894841775 * a - 1.2914855480 * b, 3);
            var r = 4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_;
            var g = -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_;
            var bl = -0.0041960863 * l_ - 0.7034186147 * m_ + 1.7076147010 * s_;
            Assert.True(new[] { r, g, bl }.All(v => v is > -0.001 and < 1.001), $"oklch({l} {c} {hDegrees}) is outside sRGB");
            return new(Encode(r), Encode(g), Encode(bl));
        }

        public static Rgb Mix(Rgb front, Rgb back, double alpha) =>
            new(front.R * alpha + back.R * (1 - alpha), front.G * alpha + back.G * (1 - alpha), front.B * alpha + back.B * (1 - alpha));

        public static double Contrast(Rgb x, Rgb y)
        {
            var (a, b) = (x.Luminance + 0.05, y.Luminance + 0.05);
            return Math.Max(a, b) / Math.Min(a, b);
        }

        private double Luminance => 0.2126 * Decode(R) + 0.7152 * Decode(G) + 0.0722 * Decode(B);

        private static double Encode(double v)
        {
            v = Math.Clamp(v, 0, 1);
            return v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        }

        private static double Decode(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
