using FluentValidation;
using FluentValidation.Results;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>Fixed texts for people; what kagent said goes to the log only.</summary>
internal static class AgentFailure
{
    public const string NotFound = "No such agent.";

    public const string Unavailable = "kagent did not accept the request (unreachable, or it refused the agent). The details are in the Skanyxx log.";

    public const string NotDeclarative = "Only Declarative agents are edited here; a BYO agent's image and deployment change where it was deployed.";

    public const string PromptSource = "This agent reads its system prompt from a ConfigMap or Secret (systemMessageFrom); edit it there, not here.";

    public const string UneditableTools = "This agent names an MCP server or agent more than once, in another namespace, or a server of another "
        + "kind; the form names each tool once in the agent's own namespace, so saving it would change what it did not show. Edit it where it was made.";

    public const string Stale = "The agent changed since you opened it. Reload it and make your change again.";

    public const string NoVersion = "Send the version the agent was read with (reload the agent before editing it).";

    public static string OtherNamespace(string ns) => $"Agents are created, edited and deleted in '{ns}' only (Studio:Namespace).";

    public static string Exists(string ns, string name) => $"An agent named '{name}' already exists in '{ns}'.";

    /// <summary>The form's problems as one 400 (RequestProblemMiddleware), keyed <c>form</c>.</summary>
    public static ValidationException Invalid(IEnumerable<string> problems) =>
        new(problems.Select(p => new ValidationFailure("form", p)));
}
