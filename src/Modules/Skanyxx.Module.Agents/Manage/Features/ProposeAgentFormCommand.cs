using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>The Agents page form, from someone who may not change a live agent, as a Studio pull request (D312).</summary>
internal sealed record ProposeAgentFormCommand(StudioUser User, AgentForm Form) : IRequest<Outcome<StudioResult>>;
