using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;
using Skanyxx.Module.Chat.Features.Agents;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>Renames the caller's own conversation in kagent; the chat index follows (<see cref="ChatConversationRenamed"/>).</summary>
internal sealed class RenameConversationHandler(KAgentApiClient kagent, IPublisher publisher, ILogger<RenameConversationHandler> logger)
    : IRequestHandler<RenameConversationCommand, Outcome<ConversationSummary>>
{
    public async Task<Outcome<ConversationSummary>> Handle(RenameConversationCommand command, CancellationToken ct)
    {
        try
        {
            var title = command.Title.Trim();
            if (await OwnConversation.FindAsync(kagent, command.UserId, command.ConversationId, ct) is not { } session
                || !await kagent.RenameSessionAsync(session.Id, command.UserId, title, ct))
                return Outcome<ConversationSummary>.NotFound(OwnConversation.NotFound);
            // kagent has the new title: the index follows even if the caller leaves now.
            await publisher.Publish(new ChatConversationRenamed(command.UserId, session.Id, title), CancellationToken.None);
            var agent = MergedAgents.FromSessionAgentId(session.AgentId)!.Value;
            return Outcome<ConversationSummary>.Ok(new ConversationSummary(session.Id, title, agent.Namespace, agent.Name, session.CreatedAt, session.LastUpdateTime));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Renaming conversation {ConversationId} failed for {UserId}", command.ConversationId, command.UserId);
            return Outcome<ConversationSummary>.Unavailable(ChatFailure.Message(ex));
        }
    }
}
