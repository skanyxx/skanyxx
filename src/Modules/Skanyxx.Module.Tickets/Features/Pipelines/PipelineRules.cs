using System.Text.RegularExpressions;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal static partial class PipelineRules
{
    public const int MaxStages = 20;
    public const int MaxAgentsPerStage = 8;
    public const int MaxLoops = 10;

    public static bool IsSlug(string? value) => value is not null && Slug().IsMatch(value);

    /// <summary><c>namespace/name</c>, both Kubernetes DNS labels.</summary>
    public static bool IsAgentRef(string? value) => value is not null && AgentRef().IsMatch(value);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}\z")]
    private static partial Regex Slug();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?/[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\z")]
    private static partial Regex AgentRef();
}
