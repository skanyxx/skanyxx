using FluentValidation;
using FluentValidation.Results;

namespace Skanyxx.Module.ToolServers.Features;

/// <summary>Fixed texts for people; what kagent said goes to the log only (it can name internals).</summary>
internal static class ToolServerFailure
{
    public const string NotFound = "No such tool server.";

    public const string KAgent = "kagent did not answer, or refused the request. The reason is in the Skanyxx log and kagent's controller log; the banner on /ToolServers says how to fix the connection.";

    public const string Create = "kagent did not create the tool server (unreachable, or it refused the resource). Nothing was saved. The reason is in the Skanyxx log and kagent's controller log.";

    public const string Recreate = "kagent removed the old server but did not create the new one. Save again; until then agents have none of its tools. The reason is in the Skanyxx log.";

    public const string Studio = "Studio keeps this server (an agent's memory or a plugin's server) and would re-create it: change it in Studio or Plugins.";

    public const string NotCreatedHere = "This server was not added on this page, so its full spec is unknown here: delete it and add it again, or change it where it was made.";

    public static string NotManaged(string kind) => kind == Definition.ToolServerKinds.Stdio
        ? "A stdio MCPServer is listed here but managed with kubectl or kmcp."
        : $"A {Definition.ToolServerKinds.Label(kind)} is listed here but managed where it was made (kubectl, GitOps).";

    public const string InUse = "Agents use this server and lose its tools when it is deleted. Confirm to delete it anyway.";

    public const string UsageUnknown = "kagent did not say which agents use this server. Confirm to delete it anyway.";

    public static string Exists(string reference) => $"A tool server named {reference} already exists.";

    /// <summary>The form's problems as one 400 (RequestProblemMiddleware), keyed <c>form</c>.</summary>
    public static ValidationException Invalid(IEnumerable<string> problems) =>
        new(problems.Select(p => new ValidationFailure("form", p)));
}
