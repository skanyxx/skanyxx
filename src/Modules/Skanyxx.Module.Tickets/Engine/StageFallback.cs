using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>What a stage writes when no agent answered: a starting point, clearly not an agent's answer.</summary>
public static class StageFallback
{
    public static string For(PipelineStage stage, Ticket ticket, IReadOnlyList<PriorStage> prior)
    {
        List<string> lines =
        [
            "> **Deterministic fallback.** No agent was reachable, so this was assembled from the ticket and",
            "> the prior stages without one. It is a starting point, not an agent's answer.",
            "",
            $"**Ticket:** {ticket.Key} — {ticket.Title}",
            $"**Type:** {Or(ticket.Type)} · **Status:** {Or(ticket.Status)}",
            ""
        ];
        lines.AddRange(stage.Kind switch
        {
            StageKind.Plan =>
            [
                "## Understanding", "", ticket.Description.Trim() is { Length: > 0 } d ? d : "_The ticket carries no description._", "",
                "## Approach", "",
                "1. Reproduce or confirm the current behaviour.",
                "2. Identify the smallest change that satisfies the title.",
                "3. Write the test that fails without that change.",
                "4. Make the change; run the test."
            ],
            StageKind.Code =>
            [
                "## Change summary", "",
                "_Not written: no agent was reachable. The prior stages are the input a developer or a later run would work from._",
                "", "## Inputs available", "",
                .. prior.Count == 0 ? ["- none: this was the first stage"] : prior.Select(p => $"- {p.Title} ({p.Kind}): {p.Output.Length} characters")
            ],
            StageKind.Qa =>
            [
                "## Verdict", "", "UNKNOWN — no agent was reachable, so nothing was reviewed.", "",
                "## What still needs a human", "", "- Everything the previous stage produced is unreviewed."
            ],
            StageKind.Review =>
            [
                "## Recommendation", "", "Not reviewed — no agent was reachable, so no review happened.", "",
                "## Stages in this run", "",
                .. prior.Count == 0 ? ["- none"] : prior.Select(p => $"- {p.StageId} ({p.Kind})")
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage.Kind, null)
        });
        return string.Join('\n', lines);
    }

    private static string Or(string value) => value.Length > 0 ? value : "unknown";
}
