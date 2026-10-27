using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>Suspend (<paramref name="Suspended"/>) or resume an agent the Agents page manages (D257).</summary>
internal sealed record SetAgentSuspendedCommand(StudioUser User, string Namespace, string Name, bool Suspended) : IRequest<Outcome<AgentDetail>>;
