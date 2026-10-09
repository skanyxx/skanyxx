using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features;

internal sealed record ListChatAgentsQuery : IRequest<Outcome<IReadOnlyList<ChatAgent>>>;
