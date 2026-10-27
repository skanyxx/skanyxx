using MediatR;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>
/// Every agent write and refused write in the identity audit (D152, D255). Names of models, servers and agents only:
/// never the system prompt.
/// </summary>
internal static class AgentAudit
{
    public const string Created = "agent.created";
    public const string Updated = "agent.updated";
    public const string Deleted = "agent.deleted";
    public const string Refused = "agent.write_refused";

    public static Task WriteAsync(IMediator mediator, string action, StudioUser user, string ns, string name, AgentForm? form = null, string? reason = null) =>
        // Never cancelled by the caller: the change already happened in kagent.
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
}
