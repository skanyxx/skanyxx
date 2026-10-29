using Skanyxx.Host.Navigation;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D390 (extends D271): the chart's ingress paths are an allow-list, so a page missing from it 404s behind the ingress.
/// Every panel the sidebar offers, and every routable page, must have its prefix; /mcp never does (validate.sh).
/// </summary>
public sealed class IngressPathsTests
{
    private static readonly string Values = File.ReadAllText(Repo.Path("deploy", "helm", "skanyxx", "values.yaml")).ReplaceLineEndings("\n");

    private static readonly string[] Prefixes =
        [.. Values[Values.IndexOf("\n  prefixPaths:\n", StringComparison.Ordinal)..].Split('\n').Skip(2)
            .TakeWhile(l => l.StartsWith("    - ", StringComparison.Ordinal)).Select(l => l["    - ".Length..])];

    private static bool Published(string path) =>
        path == "/" ? Values.Contains("  exactPaths: [\"/\"]\n") : Prefixes.Any(p => path == p || path.StartsWith(p + "/", StringComparison.Ordinal));

    [Fact]
    public void EveryNavPanel_HasAnIngressPath()
    {
        var pages = NavCatalog.Items.Select(i => i.Page).ToList();

        Assert.Contains("/Agents", pages);
        Assert.All(pages, p => Assert.True(Published(p), $"{p} has no ingress path"));
    }

    /// <summary>Index is served by the exact `/`; Error is only re-executed internally.</summary>
    [Fact]
    public void EveryPage_HasAnIngressPath()
    {
        var pages = Directory.GetFiles(Repo.Path("src", "Skanyxx.Host", "Pages"), "*.cshtml")
            .Where(f => File.ReadAllText(f).StartsWith("@page", StringComparison.Ordinal))
            .Select(f => "/" + Path.GetFileNameWithoutExtension(f))
            .Except(["/Index", "/Error"])
            .ToList();

        Assert.Contains("/ForgotPassword", pages);
        Assert.All(pages, p => Assert.True(Published(p), $"{p} has no ingress path"));
    }

    [Fact]
    public void NoPrefix_PublishesMcp() =>
        Assert.DoesNotContain(Prefixes, p => p.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase));
}
