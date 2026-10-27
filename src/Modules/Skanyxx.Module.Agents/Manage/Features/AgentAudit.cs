using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>
/// Every agent write and refused write in the identity audit (D152, D255). Names of models, servers and agents only:
/// never the system prompt. A write is audited twice (C-3): its intent before kagent is called — no row, no write — and
/// its outcome after, which never turns a change kagent applied into an error.
/// </summary>
internal static class AgentAudit
{
    public const string CreateRequested = "agent.create_requested";
    public const string UpdateRequested = "agent.update_requested";
    public const string DeleteRequested = "agent.delete_requested";
    public const string Created = "agent.created";
    public const string Updated = "agent.updated";
    public const string Deleted = "agent.deleted";
    public const string SuspendRequested = "agent.suspend_requested";
    public const string ResumeRequested = "agent.resume_requested";
    public const string Suspended = "agent.suspended";
    public const string Resumed = "agent.resumed";
    public const string Failed = "agent.write_failed";
    public const string Refused = "agent.write_refused";

    /// <summary>A refusal or an intent: a failure propagates, before anything changed (fail closed).</summary>
    public static Task WriteAsync(IMediator mediator, string action, StudioUser user, string ns, string name, AgentForm? form = null, string? reason = null) =>
        mediator.Send(new RecordAuditCommand(action, user.UserId, $"{ns}/{name}", new
        {
            @namespace = ns,
            name,
            model = form?.ModelConfig,
            mcpServers = form?.McpTools.Select(t => t.Server).ToList(),
            agentTools = form?.AgentTools,
            skills = form?.Skills.Count,
            reason
        }), CancellationToken.None);

    /// <summary>After kagent answered: the intent row already holds the attempt, so a failing row here is logged, not thrown.</summary>
    public static async Task OutcomeAsync(IMediator mediator, ILogger logger, string action, StudioUser user, string ns, string name,
        AgentForm? form = null, string? reason = null)
    {
        try
        {
            await WriteAsync(mediator, action, user, ns, name, form, reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent {Namespace}/{Name}: {Action} by {ActorUserId} was not written to the audit; its intent row stands",
                ns, name, action, user.UserId);
        }
    }
}
