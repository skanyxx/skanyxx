using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

internal sealed record DeleteAgentCommand(StudioUser User, string Namespace, string Name) : IRequest<Outcome<AgentDeleted>>;
