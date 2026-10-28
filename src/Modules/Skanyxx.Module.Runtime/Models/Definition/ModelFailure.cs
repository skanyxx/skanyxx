using FluentValidation;
using FluentValidation.Results;

namespace Skanyxx.Module.Runtime.Models.Definition;

/// <summary>Fixed texts for people. What kagent said goes to the log only: its answers may name internals.</summary>
internal static class ModelFailure
{
    public const string NotFound = "No such model.";

    public const string Unavailable = "kagent did not answer or refused the request. The details are in the Skanyxx log.";

    public static string Exists(string configRef) => $"A model named '{configRef}' already exists.";

    public static string IsDefault(string configRef) =>
        $"'{configRef}' is Skanyxx's default model (KAgent:ModelConfig): the seed agents and Chat use it. Point KAgent:ModelConfig at another model and restart Skanyxx before deleting it.";

    public static string InUse(IReadOnlyList<string> agents) =>
        $"{agents.Count} agent(s) use this model and stop working without it: {string.Join(", ", agents)}. Delete it anyway?";

    public static string KeyAgain(string provider) => $"Paste the {provider} API key again to send it to a new endpoint.";

    public static string NoKey(string provider) =>
        $"Paste the {provider} API key, or choose 'No API key' when a gateway adds it: no key is stored for this provider yet.";

    /// <summary>The form's problems as one 400 (RequestProblemMiddleware), keyed <c>form</c>.</summary>
    public static ValidationException Invalid(IEnumerable<string> problems) =>
        new(problems.Select(p => new ValidationFailure("form", p)));
}
