using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>Details of an agent Chat offers; any other agent is "not found".</summary>
internal sealed record ChatAgentDetailsQuery(string Namespace, string Name) : IRequest<Outcome<ChatAgentDetails>>;
