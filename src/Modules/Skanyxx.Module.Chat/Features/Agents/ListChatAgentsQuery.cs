using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Agents;

internal sealed record ListChatAgentsQuery : IRequest<Outcome<IReadOnlyList<ChatAgent>>>;
