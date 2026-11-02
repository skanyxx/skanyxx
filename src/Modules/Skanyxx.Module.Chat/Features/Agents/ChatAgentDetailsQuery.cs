using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>Details of an agent Chat offers; any other agent is "not found".</summary>
/// <param name="Internals">The caller reads tool server problems by name (<see cref="Caller.SeesAgentInternals"/>, D554).</param>
internal sealed record ChatAgentDetailsQuery(string Namespace, string Name, bool Internals) : IRequest<Outcome<ChatAgentDetails>>;
