using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// The direct apply and the CI install in the identity audit (D366, D362; like <see cref="Manage.Features.AgentAudit"/>): the agent's name, model,
/// tool server names and the proposal's number; never its instructions, never a token. The intent is written before git
/// is touched (no row, no write); the outcome after, where a failing row is logged rather than thrown.
/// </summary>
internal static class StudioAudit
{
    public const string ApplyRequested = "studio.apply_requested";
    public const string Applied = "studio.applied";

    /// <summary>A direct apply that opened its pull request and stopped there, waiting for checks (D367): nothing merged.</summary>
    public const string ApplyOpened = "studio.apply_opened";
    public const string ApplyRefused = "studio.apply_refused";
    public const string CiInstallRequested = "studio.ci_install_requested";
    public const string CiInstalled = "studio.ci_installed";
    public const string CiInstallFailed = "studio.ci_install_failed";

    public static Task WriteAsync(IMediator mediator, string action, StudioUser user, AgentDraft draft, int? number = null, string? reason = null) =>
        WriteAsync(mediator, action, user, $"studio/{draft.Name}", new
        {
            agent = draft.Name,
            model = draft.ModelConfig,
            mcpServers = draft.McpTools.Select(t => t.Server).ToList(),
            grants = draft.Grants.Select(g => g.Scope).ToList(),
            number,
            reason
        });

    public static Task WriteAsync(IMediator mediator, string action, StudioUser user, string target, object details) =>
        mediator.Send(new RecordAuditCommand(action, user.UserId, target, details), CancellationToken.None);

    public static Task OutcomeAsync(IMediator mediator, ILogger logger, string action, StudioUser user, AgentDraft draft, int? number = null,
        string? reason = null) =>
        OutcomeAsync(logger, action, user, draft.Name, () => WriteAsync(mediator, action, user, draft, number, reason));

    public static Task OutcomeAsync(IMediator mediator, ILogger logger, string action, StudioUser user, string target, object details) =>
        OutcomeAsync(logger, action, user, target, () => WriteAsync(mediator, action, user, target, details));

    private static async Task OutcomeAsync(ILogger logger, string action, StudioUser user, string target, Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Studio {Action} of {Target} by {ActorUserId} was not written to the audit; its intent row stands", action, target, user.UserId);
        }
    }
}
