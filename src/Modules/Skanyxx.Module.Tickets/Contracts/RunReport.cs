using System.Text;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Contracts;

/// <summary>
/// The run as one markdown document: every attempt in the order it happened, each with what the run did
/// (state), what the stage said (verdict), its warnings and its output. Readable without Skanyxx running.
/// </summary>
public static class RunReport
{
    public static string ToMarkdown(this Run run)
    {
        var md = new StringBuilder()
            .AppendLine($"# {run.TicketKey}: {run.Ticket.Title}")
            .AppendLine()
            .AppendLine($"- pipeline: {run.PipelineName} (`{run.PipelineId}`)")
            .AppendLine($"- state: {Name(run.State)}{(run.Error is null ? "" : $" — {run.Error}")}")
            .AppendLine($"- started by {run.CreatedBy} at {run.CreatedAt:u}");
        var n = 0;
        foreach (var stage in run.StageRuns.OrderBy(s => s.Id))
        {
            md.AppendLine()
                .AppendLine($"## {++n:00}. {stage.Title} — attempt {stage.Attempt}")
                .AppendLine()
                .AppendLine($"state: **{Name(stage.State)}** · verdict: **{Name(stage.Verdict)}**" +
                            $"{(stage.Degraded ? " · fallback (no agent answered)" : "")}" +
                            $"{(stage.DecidedBy is null ? "" : $" · decided by {stage.DecidedBy}")}");
            foreach (var warning in stage.Warnings)
                md.AppendLine($"> ⚠ {warning}");
            md.AppendLine().AppendLine(stage.Output.Trim());
        }

        return md.ToString();
    }

    private static string Name<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
