using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Reports;

/// <summary>What an investigation found, step by step: the JSON download, and what the PDF prints.</summary>
public sealed record RunReport(
    Guid RunId, Guid WorkflowId, string Workflow, string Input, string State, string? Failure, string StartedBy,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? EndedAt, string? Card, IReadOnlyList<ReportStep> Steps)
{
    public static RunReport From(InvestigationRun run)
    {
        var results = run.StepRuns.ToDictionary(s => s.StepId);
        return new(
            run.Id, run.WorkflowId, run.WorkflowName, run.Input, Name(run.State), run.Failure, run.CreatedBy,
            run.CreatedAt, run.StartedAt, run.EndedAt, run.CardScope is null ? null : $"{run.CardScope}/{run.CardKey}",
            [.. WorkflowTree.Flatten(run.Steps).Select(n =>
            {
                var result = results.GetValueOrDefault(n.Step.Id);
                var isAgent = n.Step.Kind == StepKind.Agent;
                return new ReportStep(
                    n.Step.Id, n.Step.Title, Name(n.Step.Kind), n.Depth, isAgent ? n.Step.Agent : null, isAgent ? null : Name(n.Step.Mode),
                    Name(result?.State ?? StepState.Pending), result?.StartedAt, result?.EndedAt,
                    result is { StartedAt: { } start, EndedAt: { } end } ? (long)(end - start).TotalMilliseconds : null,
                    result?.Output, result?.Error);
            })]);
    }

    public static string Name<T>(T value) where T : struct, Enum => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
