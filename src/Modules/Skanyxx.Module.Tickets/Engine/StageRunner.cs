using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Runs one attempt of one stage: builds the prompt, asks each agent in order (each handed the previous
/// answer), and returns the unsaved attempt. The stage's output is the LAST agent's; every agent's turn is kept.
/// An unreachable agent yields a deterministic fallback and a warning, never a hung or failed run. A run cancelled
/// while an agent works calls no further agent of the stage.
/// </summary>
public sealed class StageRunner(
    TicketsDbContext db, PromptBuilder prompts, IStageAgentClient agents, IOptions<TicketsOptions> options, TimeProvider clock)
{
    public async Task<StageRun> RunAsync(Run run, CancellationToken ct)
    {
        var stage = run.CurrentStage;
        var attempt = run.StageRuns.Count(s => s.StageId == stage.Id) + 1;
        var prior = Prior(run, stage);
        var prompt = prompts.Build(run.Ticket, stage, prior, Rejection(run, stage, attempt));
        var result = new StageRun
        {
            RunId = run.Id, StageId = stage.Id, Kind = stage.Kind, Title = stage.Name, Attempt = attempt,
            Output = "", StartedAt = Now, Warnings = [.. prompt.Warnings]
        };

        var handoff = "";
        foreach (var agent in stage.Agents)
        {
            // Cancelled while the previous agent worked: every further call would be paid for nothing. The stepper's
            // save then loses on Version, so this partial attempt is dropped.
            if (result.Turns.Count > 0 && await db.Runs.AnyAsync(r => r.Id == run.Id && r.State == RunState.Cancelled, ct))
                break;
            var message = handoff.Length > 0 ? $"{prompt.Text}\n\n{handoff}" : prompt.Text;
            if (message.Length > options.Value.MaxPromptChars)
                message = Capped(message, options.Value.MaxPromptChars, $"the message to '{agent.Id}' (prompt plus handoff) was", result.Warnings);
            var turn = new AgentTurn { AgentId = agent.Id, Agent = agent.Agent, Prompt = message, Output = "", StartedAt = Now };
            try
            {
                // Re-checked here: a run's pipeline is a snapshot, and the allow-list may have shrunk since.
                if (!options.Value.AllowedAgents.Contains(agent.Agent))
                    throw new StageAgentException("the agent is not in Tickets:AllowedAgents");
                turn.Output = Capped(await agents.AskAsync(agent, message, ct), options.Value.MaxAnswerChars,
                    $"agent '{agent.Id}' answered", result.Warnings);
            }
            catch (StageAgentException ex)
            {
                turn.Degraded = true;
                turn.Output = StageFallback.For(stage, run.Ticket, prior);
                result.Warnings.Add($"agent '{agent.Id}' ({agent.Agent}): {ex.Message}; wrote a deterministic fallback");
            }

            turn.EndedAt = Now;
            result.Turns.Add(turn);
            handoff = $"## Handed to you by {agent.Id}\n\nContinue from this. Do not restate it.\n\n" +
                      // A fresh fence: the agent that wrote this never saw its nonce.
                      new DataFence().Wrap(Capped(turn.Output, PriorStageDigestSkill.FullStageChars, $"the handoff from '{agent.Id}' was", result.Warnings));
        }

        var last = result.Turns[^1];
        result.Output = last.Output;
        result.Degraded = result.Turns.Any(t => t.Degraded);
        // A fallback's first line is fixed text, so it reads Unknown.
        result.Verdict = VerdictReader.Read(stage.Kind, last.Output);
        result.State = StateFor(stage, result);
        result.EndedAt = result.State == StageState.AwaitingHuman ? null : Now;
        return result;
    }

    /// <summary>A gate outranks a verdict: if a person will look at this, the person decides.</summary>
    private static StageState StateFor(PipelineStage stage, StageRun result)
    {
        if (stage.Gate == Gate.Human)
            return StageState.AwaitingHuman;
        if (!stage.FailOnVerdict)
            return StageState.Passed;
        // In a chain, a later agent's PASS says nothing about work an earlier agent never did.
        if (result.Degraded)
        {
            result.Warnings.Add("an agent in this stage did not answer; treated as a failure because this stage fails on its verdict");
            return StageState.Failed;
        }

        if (result.Verdict == Verdict.Pass)
            return StageState.Passed;
        // Fail closed: a stage that must pass its own check and states no verdict has not passed.
        if (result.Verdict == Verdict.Unknown)
            result.Warnings.Add("stated no verdict; treated as a failure because this stage fails on its verdict");
        return StageState.Failed;
    }

    /// <summary>
    /// Latest output of every other stage, in pipeline order. The stage's own earlier attempt is excluded:
    /// handing it its rejected work as "prior work" invites it to treat that as settled.
    /// </summary>
    internal static IReadOnlyList<PriorStage> Prior(Run run, PipelineStage stage)
    {
        var rows = run.Stages
            .Where(s => s.Id != stage.Id)
            .Select(s => Latest(run, s.Id))
            .OfType<StageRun>()
            .Where(s => s.Output.Length > 0)
            .Select(ToPrior)
            .ToList();
        return stage.Context switch
        {
            ContextMode.None => [],
            ContextMode.Last => rows.TakeLast(1).ToList(),
            _ => rows
        };
    }

    private static Rejection? Rejection(Run run, PipelineStage stage, int attempt)
    {
        if (attempt <= 1 || !run.LoopedFrom.TryGetValue(stage.Id, out var sourceId) || Latest(run, sourceId) is not { } source)
            return null;
        return new Rejection(ToPrior(source), attempt, source.Note);
    }

    private static StageRun? Latest(Run run, string stageId) =>
        run.StageRuns.Where(s => s.StageId == stageId).MaxBy(s => s.Attempt);

    private static PriorStage ToPrior(StageRun s) => new(s.StageId, s.Kind, s.Title, s.Output);

    /// <summary>Cuts text over <paramref name="limit"/>, saying so in the text and as a warning.</summary>
    private static string Capped(string text, int limit, string what, List<string> warnings)
    {
        if (text.Length <= limit)
            return text;
        warnings.Add($"{what} {text.Length:N0} characters; cut to {limit:N0}");
        return TextCut.Take(text, limit) + $"\n\n_(truncated: showing {limit:N0} of {text.Length:N0} characters)_";
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
