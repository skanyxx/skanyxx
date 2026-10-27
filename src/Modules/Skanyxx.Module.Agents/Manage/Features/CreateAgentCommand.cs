using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

internal sealed record CreateAgentCommand(StudioUser User, AgentForm Form) : IRequest<Outcome<AgentDetail>>;
