using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Audit;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Endpoints;
using Skanyxx.Module.Investigate.Features;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Engine;

/// <summary>
/// Runs one claimed run to its end (D342): top-level steps one after another, a group's steps in its mode, each agent
/// step one kagent turn under its own timeout. The first failed step fails the run with a reason a person can read and
/// cancels whatever else is running; steps never reached are "skipped". A cancel (asked through the database, so any
/// replica's API can ask) and a host shutdown end it the same way. A step that cannot be recorded (a database error)
/// fails the run at once rather than leaving its siblings to their timeouts. Answers and reasons pass through
/// <see cref="SecretText.Redact"/> before they are stored or read by a later step (D373). A success leaves a memory
/// card. Every step's turn is admitted against the starter's token quota before kagent is asked (a refusal fails the
/// step) and counted to them afterwards, also when it failed, timed out or was cancelled (D385), priced on the agent's
/// ModelConfig when its spec names one. Before the first step the agents are checked again (D386): one may have gained a
/// write tool or grant, or been suspended, while the run was queued. The starter is checked too, at pickup and before
/// each agent step (D395): a person disabled or no longer a builder stops being able to spend on their earlier
/// authority, whichever replica holds the run. Never throws.
/// </summary>
internal sealed partial class RunExecutor(
    IDbContextFactory<InvestigateDbContext> dbs,
    IInvestigationAgentClient agents,
    IChatQuota quota,
    AgentCheck check,
    StepWriter steps,
    InvestigateAudit audit,
    ICardExtractor cards,
    IPersonStanding standing,
    IOptions<InvestigateOptions> options,
    TimeProvider clock,
    ILogger<RunExecutor> logger)
{
    private const string StarterRefused = "The person who started this run may no longer run investigations (disabled, or no longer a builder).";

    private readonly InvestigateOptions _options = options.Value;

    public async Task ExecuteAsync(Guid runId, CancellationToken stopping)
    {
        try
        {
            InvestigationRun run;
            await using (var db = await dbs.CreateDbContextAsync(stopping))
                run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, stopping);

            var (refusal, allowed) = await MayRunAsync(run.CreatedBy, stopping)
                ? await CheckAgentsAsync(run, stopping)
                : (StarterRefused, []);
            if (refusal is not null)
            {
                await EndAsync(runId, RunState.Failed, refusal);
                await audit.RunAsync(InvestigateAudit.RunFinished, run.CreatedBy, run.Id, run.WorkflowId, run.WorkflowName, "failed");
                return;
            }

            var models = allowed.ToDictionary(a => a.Ref, a => ChatQuotaUsage.ModelRef(a.Namespace, a.ModelConfig));
            using var context = new RunContext(runId, run.CreatedBy, run.Input, models, _options.MaxParallelSteps, stopping);
            using var watching = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            var watch = WatchAsync(context, watching.Token);
            try
            {
                await RunStepsAsync(run.Steps, GroupMode.Sequential, context);
            }
            finally
            {
                await watching.CancelAsync();
                await watch;
            }

            await FinishAsync(run, context);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            await EndAsync(runId, RunState.Failed, "Skanyxx stopped while this run was in progress; run it again.");
        }
        catch (Exception ex)
        {
            LogRunCrashed(logger, runId, ex);
            await EndAsync(runId, RunState.Failed, $"The run stopped on an internal error ({ex.GetType().Name}).");
        }
    }

    private async Task<bool> MayRunAsync(string userId, CancellationToken ct) =>
        (await standing.ActiveRolesAsync(userId, ct)).Overlaps(InvestigateGroup.Roles);

    /// <summary>The start's check, with the workflow's allowance as it is now (none once the workflow is deleted).</summary>
    private async Task<(string? Refusal, IReadOnlyList<AgentChoice> Allowed)> CheckAgentsAsync(InvestigationRun run, CancellationToken ct)
    {
        bool allow;
        await using (var db = await dbs.CreateDbContextAsync(ct))
            allow = await db.Workflows.Where(w => w.Id == run.WorkflowId).Select(w => w.AllowChainedWrites).SingleOrDefaultAsync(ct);
        var (refusal, allowed) = await check.CheckAsync<bool>(run.Steps, allow, ct);
        return (refusal?.Message, allowed);
    }

    /// <returns>Succeeded when every step did; otherwise the state of the step that stopped the sequence.</returns>
    private async Task<StepState> RunStepsAsync(IReadOnlyList<WorkflowStep> children, GroupMode mode, RunContext context)
    {
        if (mode == GroupMode.Parallel)
        {
            var states = await Task.WhenAll(children.Select(c => RunStepAsync(c, context)));
            return states.Contains(StepState.Failed) ? StepState.Failed
                : states.All(s => s == StepState.Succeeded) ? StepState.Succeeded : StepState.Cancelled;
        }

        foreach (var child in children)
        {
            var state = await RunStepAsync(child, context);
            if (state != StepState.Succeeded)
                return state;
        }
        return StepState.Succeeded;
    }

    private async Task<StepState> RunStepAsync(WorkflowStep step, RunContext context)
    {
        try
        {
            return step.Kind == StepKind.Group ? await RunGroupAsync(step, context) : await RunAgentAsync(step, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The step's own row could not be written (or another internal error): stop the whole run now.
            LogStepCrashed(logger, context.RunId, step.Id, ex);
            context.Fail($"Step '{step.Title}' stopped on an internal error ({ex.GetType().Name}).");
            return StepState.Failed;
        }
    }

    private async Task<StepState> RunGroupAsync(WorkflowStep step, RunContext context)
    {
        // Never started: stays pending, and the end of the run marks it skipped.
        if (context.Token.IsCancellationRequested)
            return StepState.Cancelled;
        await steps.StartedAsync(context.RunId, step.Id);
        var state = await RunStepsAsync(step.Steps, step.Mode, context);
        await steps.EndedAsync(context.RunId, step.Id, state);
        return state;
    }

    private async Task<StepState> RunAgentAsync(WorkflowStep step, RunContext context)
    {
        try
        {
            await context.Slots.WaitAsync(context.Token);
        }
        catch (OperationCanceledException)
        {
            return StepState.Cancelled; // never started: stays pending, then skipped
        }

        try
        {
            // Not the run's token: a cancel already ends the run, and this is one short query.
            if (!await MayRunAsync(context.StartedBy, CancellationToken.None))
            {
                context.Fail(StarterRefused);
                return StepState.Failed; // never started: stays pending, then skipped
            }
            await steps.StartedAsync(context.RunId, step.Id);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(_options.StepTimeoutSeconds));
            var prompt = PromptTemplate.Render(step.Prompt!, context.Input, context.Outputs, _options.MaxRenderedPromptChars);
            var asked = false;
            AgentAnswer? answer = null;
            try
            {
                // Released at once, as for a ticket run: this run's own parallel steps (up to MaxParallelSteps) must not
                // take the person's in-flight places and refuse each other (D385).
                using (var admission = await quota.AdmitAsync(context.StartedBy, step.Agent!, deadline.Token))
                    if (admission.Refusal is { } refusal)
                        return await FailAsync(step, context, refusal.TrimEnd('.'));
                asked = true;
                answer = await agents.AskAsync(step.Agent!, prompt, deadline.Token);
                var output = SecretText.Redact(TextCut.Take(answer.Text, _options.MaxOutputChars));
                context.Outputs[step.Id] = output;
                await steps.EndedAsync(context.RunId, step.Id, StepState.Succeeded, output);
                return StepState.Succeeded;
            }
            catch (OperationCanceledException) when (!context.Token.IsCancellationRequested)
            {
                return await FailAsync(step, context, $"timed out after {_options.StepTimeoutSeconds} s");
            }
            catch (OperationCanceledException)
            {
                await steps.EndedAsync(context.RunId, step.Id, StepState.Cancelled);
                return StepState.Cancelled;
            }
            catch (AgentCallException ex)
            {
                return await FailAsync(step, context, ex.Message);
            }
            finally
            {
                // Not the run's token: a timed-out or cancelled call was paid for too.
                if (asked)
                    await quota.RecordAsync(ChatQuotaUsage.Measure(context.StartedBy, step.Agent!, context.Models[step.Agent!],
                        context.RunId.ToString(), answer?.Usage ?? ChatUsage.None, prompt, answer?.Text ?? ""), CancellationToken.None);
            }
        }
        finally
        {
            context.Slots.Release();
        }
    }

    private async Task<StepState> FailAsync(WorkflowStep step, RunContext context, string reason)
    {
        reason = SecretText.Redact(reason);
        context.Fail($"Step '{step.Title}' ({step.Agent}) failed: {reason}.");
        await steps.EndedAsync(context.RunId, step.Id, StepState.Failed, error: reason);
        return StepState.Failed;
    }

    /// <summary>Stamps the heartbeat and looks for a cancel, until the run ends. A database blip is logged and retried.</summary>
    private async Task WatchAsync(RunContext context, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.WatchMilliseconds), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await using var db = await dbs.CreateDbContextAsync(ct);
                    // The database's clock, as the stale sweep reads it: replicas' clocks may disagree (D373).
                    await db.Runs.Where(r => r.Id == context.RunId).ExecuteUpdateAsync(s => s.SetProperty(r => r.HeartbeatAt, r => DateTime.UtcNow), ct);
                    if (await db.Runs.Where(r => r.Id == context.RunId).Select(r => r.CancelRequested).SingleAsync(ct))
                    {
                        context.CancelByUser();
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogWatchFailed(logger, context.RunId, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended (or the host is stopping).
        }
    }

    private async Task FinishAsync(InvestigationRun run, RunContext context)
    {
        var (state, reason) = context.Failure is { } failure ? (RunState.Failed, failure)
            : context.CancelledByUser ? (RunState.Cancelled, "Cancelled.")
            : context.Token.IsCancellationRequested ? (RunState.Failed, "Skanyxx stopped while this run was in progress; run it again.")
            : (RunState.Succeeded, (string?)null);

        MemoryCardRef? card = null;
        if (state == RunState.Succeeded)
            card = await cards.InvestigationAsync(
                new InvestigationFinished(run.Id, run.CreatedBy, run.WorkflowName, run.Input, Finding(run.Steps, context)), CancellationToken.None);

        await EndAsync(run.Id, state, reason, card);
        await audit.RunAsync(InvestigateAudit.RunFinished, run.CreatedBy, run.Id, run.WorkflowId, run.WorkflowName, state.ToString().ToLowerInvariant());
    }

    /// <summary>The finding is the answer of the last agent step, in tree order.</summary>
    private static string Finding(IReadOnlyList<WorkflowStep> tree, RunContext context) =>
        WorkflowTree.Flatten(tree).Reverse().Select(n => context.Outputs.GetValueOrDefault(n.Step.Id)).FirstOrDefault(o => o is not null) ?? "";

    private async Task EndAsync(Guid runId, RunState state, string? reason, MemoryCardRef? card = null)
    {
        try
        {
            await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
            await db.StepRuns.Where(s => s.RunId == runId && s.State == StepState.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Skipped), CancellationToken.None);
            await db.StepRuns.Where(s => s.RunId == runId && s.State == StepState.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Cancelled).SetProperty(x => x.EndedAt, x => DateTime.UtcNow), CancellationToken.None);
            await db.Runs.Where(r => r.Id == runId && r.State == RunState.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.State, state)
                    .SetProperty(r => r.Failure, reason == null ? null : TextCut.Take(SecretText.Redact(reason), 900))
                    .SetProperty(r => r.EndedAt, r => DateTime.UtcNow) // the database's clock, as StartedAt (D373)
                    .SetProperty(r => r.CardScope, card == null ? null : card.Scope)
                    .SetProperty(r => r.CardKey, card == null ? null : card.Key), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The heartbeat stops with this replica's attention, so the stale sweep fails it later.
            LogEndNotSaved(logger, runId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Investigation run {RunId} stopped on an internal error")]
    private static partial void LogRunCrashed(ILogger logger, Guid runId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Investigation run {RunId}: step {StepId} stopped on an internal error")]
    private static partial void LogStepCrashed(ILogger logger, Guid runId, string stepId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Investigation run {RunId}: the heartbeat or cancel check failed; retrying")]
    private static partial void LogWatchFailed(ILogger logger, Guid runId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Investigation run {RunId}: its end could not be saved")]
    private static partial void LogEndNotSaved(ILogger logger, Guid runId, Exception ex);
}
