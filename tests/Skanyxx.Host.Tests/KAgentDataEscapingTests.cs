using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D246 (Chrome run): the Agents page put an Agent's name and description into innerHTML as is. Those strings are
/// written by whoever can edit an Agent in kagent, so a description like <c>&lt;img onerror=…&gt;</c> ran script in the
/// owner's session. Every value from kagent goes through the page's escaping function (Tool Servers: D245).
/// </summary>
public sealed class KAgentDataEscapingTests
{
    [Theory]
    [InlineData("Agents.cshtml", "agent", "escapeHtml")]
    [InlineData("ToolServers.cshtml", "server", "esc")]
    [InlineData("ToolServers.cshtml", "tool", "esc")]
    public void KAgentValues_AreEscapedBeforeTheyReachTheHtml(string page, string variable, string escape)
    {
        var source = File.ReadAllText(Repo.Path("src", "Skanyxx.Host", "Pages", page));
        // A value spliced into markup: ${agent.x} in a template, or '...' + server.x in a concatenation (conditions excluded).
        var raw = new Regex(@"(\$\{|'\s*\+\s*\(?)\s*" + variable + @"\.(?!status\b|discoveredTools\b)\w+(?!\w*\s*\?)")
            .Matches(source).Select(m => m.Value).ToList();

        Assert.Contains($"function {escape}(", source);
        Assert.Contains("'\"': '&quot;'", source); // safe inside attributes too
        Assert.Empty(raw);
    }
}
