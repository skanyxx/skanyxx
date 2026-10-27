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

    public const string ForeignTools = "This agent uses an MCP server or agent in another namespace, or a server of another kind; the form "
        + "names tools in the agent's own namespace only, so edit it where it was made.";

    public static string OtherNamespace(string ns) => $"Agents are created, edited and deleted in '{ns}' only (Studio:Namespace).";

    public static string Exists(string ns, string name) => $"An agent named '{name}' already exists in '{ns}'.";

    /// <summary>The form's problems as one 400 (RequestProblemMiddleware), keyed <c>form</c>.</summary>
    public static ValidationException Invalid(IEnumerable<string> problems) =>
        new(problems.Select(p => new ValidationFailure("form", p)));
}
