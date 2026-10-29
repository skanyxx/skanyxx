using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Investigate.Audit;

/// <summary>
/// Every workflow change and run in the identity audit (D344). A row holds ids, names, counts and states only: never a
/// prompt, an input, an output or a failure text (which may quote an agent). The change it records has already
/// happened, so a failed audit write is logged, never thrown.
/// </summary>
internal sealed partial class InvestigateAudit(IMediator mediator, ILogger<InvestigateAudit> logger)
{
    public const string WorkflowCreated = "investigate.workflow_created";
    public const string WorkflowUpdated = "investigate.workflow_updated";
    public const string WorkflowDuplicated = "investigate.workflow_duplicated";
    public const string WorkflowDeleted = "investigate.workflow_deleted";
    public const string RunStarted = "investigate.run_started";
    public const string RunCancelRequested = "investigate.run_cancel_requested";
    public const string RunFinished = "investigate.run_finished";

    public Task WorkflowAsync(string action, string actorId, Guid workflowId, string name, int steps, string? sharedTeam) =>
        WriteAsync(action, actorId, $"workflow:{workflowId}", new { workflowId, name, steps, sharedTeam });

    public Task RunAsync(string action, string actorId, Guid runId, Guid workflowId, string workflowName, string state) =>
        WriteAsync(action, actorId, $"run:{runId}", new { runId, workflowId, workflowName, state });

    private async Task WriteAsync(string action, string actorId, string target, object details)
    {
        try
        {
            await mediator.Send(new RecordAuditCommand(action, actorId, target, details), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogNotAudited(logger, action, target, actorId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Action} on {Target} by {ActorId} was not written to the audit")]
    private static partial void LogNotAudited(ILogger logger, string action, string target, string actorId, Exception ex);
}
