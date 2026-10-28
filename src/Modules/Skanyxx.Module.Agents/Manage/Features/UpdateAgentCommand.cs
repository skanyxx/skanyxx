using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>The agent is <see cref="AgentForm.Namespace"/>/<see cref="AgentForm.Name"/> (from the route).</summary>
internal sealed record UpdateAgentCommand(StudioUser User, AgentForm Form) : IRequest<Outcome<AgentDetail>>;
