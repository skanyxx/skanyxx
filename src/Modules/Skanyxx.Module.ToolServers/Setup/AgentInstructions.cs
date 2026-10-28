using System.Text;

namespace Skanyxx.Module.ToolServers.Setup;

/// <summary>
/// The "Copy instructions for agent" text: a prompt an AI coding agent can act on. Built from the fix's own text and
/// <see cref="FixFacts"/>, which hold names only, so it can never carry a header value or a token.
/// </summary>
internal static class AgentInstructions
{
    public static string For(string goal, string title, string cause, FixFacts facts,
        IReadOnlyList<FixStep> local, IReadOnlyList<FixStep> helm, IReadOnlyList<FixStep> verify)
    {
        var text = new StringBuilder();
        text.AppendLine($"Fix a Skanyxx tool server problem: {title}");
        text.AppendLine("Work in the Skanyxx repository and the Kubernetes cluster kagent runs in. The Tool servers page is /ToolServers; kagent is 0.10.2.");
        text.AppendLine();
        text.AppendLine($"Goal: {goal}");
        text.AppendLine();
        text.AppendLine("Detected state (names only, no secret values):");
        foreach (var fact in Lines(facts))
            text.AppendLine($"- {fact}");
        text.AppendLine();
        text.AppendLine($"Cause: {cause}");
        Section(text, "Steps: local development", local);
        Section(text, "Steps: Helm install", helm);
        Section(text, "Verify", verify);
        text.AppendLine();
        text.AppendLine("Rules:");
        text.AppendLine("- Never print, log, commit or paste a secret value (token, password, API key). Refer to Secrets by name and key; create them from a file, never with the value on the command line.");
        text.AppendLine("- Do not read a Secret's data to check it: list its keys only.");
        text.AppendLine("- Ask before changing a shared cluster; replace every <placeholder> with the real name first.");
        return text.ToString();
    }

    private static IEnumerable<string> Lines(FixFacts facts)
    {
        yield return $"Skanyxx calls kagent's controller at {facts.KAgentAddress} (settings KAgent:BaseUrl and KAgent:Port, or KAgent:IngressUrl).";
        if (facts.KAgentStatus is { } status)
            yield return $"kagent answered HTTP {status}; its message is in the Skanyxx log (not copied here).";
        if (facts.Ref is { } reference)
            yield return $"Tool server: {reference} ({Definition.ToolServerKinds.Label(facts.Kind ?? "")}).";
        if (facts.UrlOrigin is { } origin)
            yield return $"Server address: {origin} (path left out){(facts.Protocol is { } p ? $", protocol {p}" : "")}.";
        if (facts.LoopbackUrl)
            yield return "The server URL points at a loopback address.";
        foreach (var header in facts.SecretHeaders)
            yield return $"Header {header.Name} is read from Secret {header.SecretName}, key {header.SecretKey}, in namespace {facts.Namespace}.";
        if (facts.ToolCount is { } count)
            yield return $"Tools kagent discovered on it: {count}.";
        if (facts.UsedBy is { Count: > 0 } agents)
            yield return $"Agents that use it: {string.Join(", ", agents)}.";
    }

    private static void Section(StringBuilder text, string title, IReadOnlyList<FixStep> steps)
    {
        text.AppendLine();
        text.AppendLine($"{title}:");
        for (var i = 0; i < steps.Count; i++)
        {
            text.AppendLine($"{i + 1}. {steps[i].Text}");
            if (steps[i].Command is { } command)
                text.AppendLine($"   $ {command}");
        }
    }
}
