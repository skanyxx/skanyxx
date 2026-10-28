using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Features.Conversations;

internal sealed class DeleteConversationHandler(KAgentApiClient kagent, ChatFiles files, IPublisher publisher, ILogger<DeleteConversationHandler> logger)
    : IRequestHandler<DeleteConversationCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(DeleteConversationCommand command, CancellationToken ct)
    {
        try
        {
            if (await OwnConversation.FindAsync(kagent, command.UserId, command.ConversationId, ct) is not { } session)
                return Outcome<bool>.NotFound(OwnConversation.NotFound);
            await kagent.DeleteSessionAsync(session.Id, command.UserId, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Deleting conversation {ConversationId} failed for {UserId}", command.ConversationId, command.UserId);
            return Outcome<bool>.Unavailable(ChatFailure.Message(ex));
        }
        // kagent's session is gone: what Skanyxx kept about it goes too, even if the caller leaves now.
        await files.DeleteConversationAsync(command.UserId, command.ConversationId, CancellationToken.None);
        await publisher.Publish(new ChatConversationDeleted(command.UserId, command.ConversationId), CancellationToken.None);
        return Outcome<bool>.Ok(true);
    }
}
