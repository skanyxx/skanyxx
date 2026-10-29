using System.Text.RegularExpressions;
using Skanyxx.Host.Navigation;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Every page is on the design system (review/ux-review-1.md, "Rules for page authors"): its look comes from tokens.css,
/// components.css and ui.js, so it follows all eight themes and keeps their contrast. A page-local style block, an inline
/// handler, the browser's own confirm/prompt, or a colour typed into a page would each escape that, so they are refused here.
/// </summary>
public sealed partial class DesignSystemPagesTests
{
    private static readonly string Pages = Repo.Path("src", "Skanyxx.Host", "Pages");
    private static readonly string WebRoot = Repo.Path("src", "Skanyxx.Host", "wwwroot");

    public static TheoryData<string> PageFiles() =>
        [.. Directory.GetFiles(Pages, "*.cshtml", SearchOption.AllDirectories).Select(f => System.IO.Path.GetRelativePath(Repo.Root, f))];

    public static TheoryData<string> StyleSheets() =>
        [.. Directory.GetFiles(System.IO.Path.Combine(WebRoot, "css"), "*.css")
            .Where(f => System.IO.Path.GetFileName(f) != "tokens.css")
            .Select(f => System.IO.Path.GetRelativePath(Repo.Root, f))];

    public static TheoryData<string> Scripts() =>
        [.. Directory.GetFiles(System.IO.Path.Combine(WebRoot, "js"), "*.js").Select(f => System.IO.Path.GetRelativePath(Repo.Root, f))];

    [Theory]
    [MemberData(nameof(PageFiles))]
    public void APage_HasNoStyleBlock_AndNoInlineHandler(string page)
    {
        var text = File.ReadAllText(Repo.Path(page));

        Assert.DoesNotContain("<style", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(InlineHandler().IsMatch(text), $"{page}: an on…= attribute; wire it in script (data-once, data-confirm, data-dialog-open)");
    }

    [Theory]
    [MemberData(nameof(PageFiles))]
    public void APage_UsesTheStyledDialogs_NotTheBrowsers(string page) =>
        Assert.False(NativeDialog().IsMatch(File.ReadAllText(Repo.Path(page))), $"{page}: use skx.confirm, data-confirm or a <dialog>, not confirm()/prompt()/alert()");

    [Theory]
    [MemberData(nameof(Scripts))]
    public void AScript_UsesTheStyledDialogs_NotTheBrowsers(string script)
    {
        // ui.js itself falls back to window.confirm where <dialog> is missing.
        if (script.EndsWith("ui.js", StringComparison.Ordinal))
            return;
        var text = File.ReadAllText(Repo.Path(script));
        // A script's own function of that name (setup-guide.js builds a prompt for an agent) is not the browser's.
        var own = OwnFunction().Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
        var calls = NativeDialog().Matches(text).Select(m => m.Groups[1].Value).Where(name => !own.Contains(name)).ToList();
        Assert.True(calls.Count == 0, $"{script}: use skx.confirm, not {string.Join(", ", calls)}()");
    }

    [Theory]
    [MemberData(nameof(StyleSheets))]
    public void AStyleSheet_TakesEveryColourFromTheTokens(string sheet)
    {
        var offending = File.ReadAllLines(Repo.Path(sheet))
            .Where(line => CssColour().IsMatch(line))
            // The "system" theme swatch shows the light and the dark half side by side, so it names both colours.
            .Where(line => !line.Contains(".swatch-system", StringComparison.Ordinal))
            .ToList();

        Assert.True(offending.Count == 0, $"{sheet}: colours outside tokens.css:\n{string.Join("\n", offending)}");
    }

    [Theory]
    [MemberData(nameof(PageFiles))]
    public void APage_TypesNoColour(string page)
    {
        var offending = File.ReadAllLines(Repo.Path(page))
            // The browser's own bar colour is a meta tag, which cannot read a CSS variable.
            .Where(line => !line.Contains("name=\"theme-color\"", StringComparison.Ordinal))
            .Where(line => MarkupColour().IsMatch(line))
            .ToList();

        Assert.True(offending.Count == 0, $"{page}: colours belong in tokens.css:\n{string.Join("\n", offending)}");
    }

    /// <summary>The sidebar (NavCatalog) is the one place that links panels; a page links another panel only with context (an id, a query).</summary>
    [Theory]
    [MemberData(nameof(PageFiles))]
    public void APage_HasNoNavLinkOfItsOwn(string page)
    {
        var text = File.ReadAllText(Repo.Path(page));
        var links = NavCatalog.Items.Where(i => text.Contains($"href=\"{i.Page}\"", StringComparison.OrdinalIgnoreCase)).Select(i => i.Page).ToList();

        Assert.True(links.Count == 0, $"{page} links {string.Join(", ", links)} itself; the sidebar does");
    }

    /// <summary>An undefined custom property falls back silently (the old pages used --info-color, which no sheet set).</summary>
    [Fact]
    public void EveryCustomProperty_APageOrSheetReads_IsDefined()
    {
        var sheets = Directory.GetFiles(System.IO.Path.Combine(WebRoot, "css"), "*.css");
        var defined = sheets.SelectMany(f => Definition().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToHashSet();
        var readers = sheets
            .Concat(Directory.GetFiles(System.IO.Path.Combine(WebRoot, "js"), "*.js"))
            .Concat(Directory.GetFiles(Pages, "*.cshtml", SearchOption.AllDirectories));
        var missing = readers
            .SelectMany(f => Use().Matches(File.ReadAllText(f)).Select(m => (File: System.IO.Path.GetFileName(f), Name: m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)))
            .Where(u => !defined.Contains(u.Name))
            .Select(u => $"{u.File}: {u.Name}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, $"read but never defined:\n{string.Join("\n", missing)}");
    }

    [Fact]
    public void TheOldServiceWorker_IsGone_AndNothingRegistersOne()
    {
        Assert.False(File.Exists(System.IO.Path.Combine(WebRoot, "serviceworker.js")));
        foreach (var file in Directory.GetFiles(System.IO.Path.Combine(WebRoot, "js"), "*.js").Concat(Directory.GetFiles(Pages, "*.cshtml", SearchOption.AllDirectories)))
            Assert.DoesNotContain("serviceWorker.register", File.ReadAllText(file));
    }

    [Theory]
    [InlineData(".x { color: #fff; }", true)]
    [InlineData(".x { background: rgba(0, 0, 0, 0.6); }", true)]
    [InlineData(".x { color: white; }", true)]
    [InlineData(".x { color: oklch(1 0 0); }", true)]
    [InlineData(".x { color: var(--text); white-space: nowrap; }", false)]
    [InlineData(".x { grid-column: 1 / -1; }", false)]
    public void TheColourCheck_FindsALiteral(string line, bool found) => Assert.Equal(found, CssColour().IsMatch(line));

    [GeneratedRegex(@"(--[a-z0-9-]+)\s*:")]
    private static partial Regex Definition();

    // var(--x) in a sheet or a style, and '--x' handed to getPropertyValue by a chart script.
    [GeneratedRegex(@"var\((--[a-z0-9-]+)|['""](--[a-z0-9]+(?:-[a-z0-9]+)*)['""]")]
    private static partial Regex Use();

    [GeneratedRegex(@"\son[a-z]+\s*=\s*""", RegexOptions.IgnoreCase)]
    private static partial Regex InlineHandler();

    [GeneratedRegex(@"(?<![\w.$])(confirm|prompt|alert)\(")]
    private static partial Regex NativeDialog();

    [GeneratedRegex(@"function\s+(confirm|prompt|alert)\s*\(")]
    private static partial Regex OwnFunction();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\b(rgba?|hsla?|oklch|oklab|lab|lch|hwb|color-mix)\(|:\s*(white|black)\b")]
    private static partial Regex CssColour();

    [GeneratedRegex(@"(color|background|border|fill|stroke)[a-z-]*\s*:\s*(#[0-9a-fA-F]{3,8}\b|(rgba?|hsla?|oklch)\(|(white|black)\b)|\b(rgba?|hsla?)\(", RegexOptions.IgnoreCase)]
    private static partial Regex MarkupColour();
}
