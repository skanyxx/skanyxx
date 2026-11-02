using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <param name="Internals">The caller reads tool server problems by name (<see cref="Caller.SeesAgentInternals"/>, D554).</param>
internal sealed record ListChatAgentsQuery(bool Internals) : IRequest<Outcome<IReadOnlyList<ChatAgent>>>;
