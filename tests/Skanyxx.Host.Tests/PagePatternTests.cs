using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Browsers compile an input's <c>pattern</c> with the <c>v</c> flag, where a literal <c>-</c> in a character class must
/// be escaped (<c>[a-z0-9-]</c> is a syntax error). Chrome then logs an error and skips the check, so the form posts
/// whatever was typed (Chrome walkthrough 2026-10-28: Org, Studio, Tool Servers).
/// </summary>
public sealed partial class PagePatternTests
{
    public static TheoryData<string> Pages() =>
        [.. Directory.GetFiles(Repo.Path("src", "Skanyxx.Host", "Pages"), "*.cshtml", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(Repo.Root, f))];

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryLiteralPattern_IsValidWithTheVFlag(string page)
    {
        foreach (Match m in PatternAttribute().Matches(File.ReadAllText(Repo.Path(page))))
        {
            var pattern = m.Groups[1].Value.Replace("@@", "@");
            Assert.True(UnescapedHyphenInClass(pattern) is null, $"{page}: pattern=\"{pattern}\" has an unescaped '-' in a character class");
        }
    }

    [Theory]
    [InlineData("[a-z0-9-]+", true)]
    [InlineData("[-a-z0-9]", true)]
    [InlineData("[a-z0-9._@-]{0,127}", true)]
    [InlineData("[a-z-_]", true)]
    [InlineData("[0-9-.]", true)]
    [InlineData("[a-z0-9._@\\-]{0,127}", false)]
    [InlineData("[a-z0-9]([a-z0-9\\-]*[a-z0-9])?", false)]
    [InlineData("[0-9a-f]{40}", false)]
    [InlineData("a-b", false)]
    public void TheCheck_FindsALiteralHyphen(string pattern, bool invalid) =>
        Assert.Equal(invalid, UnescapedHyphenInClass(pattern) is not null);

    /// <summary>The index of a <c>-</c> in a class that is not a range between two single characters, or null.</summary>
    private static int? UnescapedHyphenInClass(string pattern)
    {
        var inClass = false;
        // Whether the previous class member can start a range: a single character that does not already end one.
        var canStartRange = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (!inClass)
            {
                if (c == '\\') i++;
                else if (c == '[') { inClass = true; canStartRange = false; }
                continue;
            }
            if (c == ']') { inClass = false; continue; }
            if (c == '-')
            {
                var next = i + 1 < pattern.Length ? pattern[i + 1] : ']';
                if (!canStartRange || next is ']' or '-') return i;
                i += next == '\\' ? 2 : 1; // the range's end
                canStartRange = false;
                continue;
            }
            if (c == '\\') i++;
            canStartRange = true;
        }
        return null;
    }

    // Literal patterns only: one bound from C# (pattern="@CardFormat.KeyPattern") is checked by its own value below.
    [GeneratedRegex("pattern=\"([^\"@][^\"]*)\"")]
    private static partial Regex PatternAttribute();

    [Fact]
    public void CardKeyPattern_IsValidWithTheVFlag() =>
        Assert.Null(UnescapedHyphenInClass(Skanyxx.Core.Platform.Memory.CardFormat.KeyPattern));
}
