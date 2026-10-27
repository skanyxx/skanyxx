using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// What memory is told when a run ends (requirements.md §3.2): the ticket, how it ended and a short outcome — the last
/// stage's output, cut — never the run's prompts or every stage's answer. A cancelled run is not finished work.
/// </summary>
public static class RunFinishedNotice
{
    public const int OutcomeChars = 1_000;
    public const int RelatedTickets = 10;

    public static TicketRunFinished? From(Run run)
    {
        if (run.State is not (RunState.Succeeded or RunState.Failed))
            return null;

        var last = run.StageRuns.MaxBy(s => s.Id);
        var outcome = run.Error ?? (last is null ? "" : $"{last.Title} ({last.Verdict.ToString().ToLowerInvariant()}): {last.Output}");
        var related = CardTickets.FindIn($"{run.Ticket.Title}\n{run.Ticket.Description}", RelatedTickets + 1)
            .Where(k => k != run.TicketKey).Take(RelatedTickets).ToList();
        return new TicketRunFinished(
            run.Id, run.CreatedBy, run.TicketKey, run.Ticket.Title, run.Ticket.Url, run.PipelineName,
            run.State == RunState.Succeeded, TextCut.Take(outcome, OutcomeChars), related, run.Ticket.Labels);
    }
}
