using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Turns;

internal sealed class StreamChatMessageHandler(KAgentApiClient kagent, ChatTurns turns, ILogger<ChatStream> logger)
    : IRequestHandler<StreamChatMessageCommand, Outcome<ChatStream>>
{
    public async Task<Outcome<ChatStream>> Handle(StreamChatMessageCommand command, CancellationToken ct)
    {
        try
        {
            return (await turns.StartAsync(command.Turn, ct)).Map(start => new ChatStream(command.Turn, start, kagent, turns, logger));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Chat with {Namespace}/{Agent} failed for {UserId}", command.Turn.AgentNamespace, command.Turn.AgentName, command.Turn.UserId);
            return Outcome<ChatStream>.Unavailable(ChatFailure.Message(ex));
        }
    }
}
