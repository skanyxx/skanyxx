using System.Text.RegularExpressions;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Who proposed a pull request, written by Skanyxx into its body. Trusted because Skanyxx's account is the only one
/// that can open one (registration off, research/gitea-api.md).
/// </summary>
internal static partial class ProposalTrailer
{
    public static string Write(string agent, string userId, string description) =>
        $"Agent: {agent}\nProposed-by: {userId}\n\n{description}\n\nProposed in Skanyxx studio. A supervisor merges in Skanyxx; the reconciler then applies it to kagent.";

    public static string? ProposedBy(string body) => Line().Match(body) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"^Proposed-by: ([a-z0-9][a-z0-9._@-]{0,127})$", RegexOptions.Multiline)]
    private static partial Regex Line();
}
