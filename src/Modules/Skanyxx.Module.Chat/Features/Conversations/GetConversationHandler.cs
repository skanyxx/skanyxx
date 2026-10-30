using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Core.Services;
using Skanyxx.Module.Chat.Features.Agents;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>A reload or a sidebar pick reopens the conversation from kagent (the source of truth), never a new one.</summary>
internal sealed class GetConversationHandler(KAgentApiClient kagent, ITokenEstimator estimator, ILogger<GetConversationHandler> logger)
    : IRequestHandler<GetConversationQuery, Outcome<ConversationView>>
{
    public async Task<Outcome<ConversationView>> Handle(GetConversationQuery query, CancellationToken ct)
    {
        try
        {
            var envelope = await kagent.GetSessionWithEventsAsync(query.ConversationId, query.UserId, ct);
            if (envelope?.Session is not { } session || !OwnConversation.IsOwn(session, query.UserId))
                return Outcome<ConversationView>.NotFound(OwnConversation.NotFound);
            var agent = MergedAgents.FromSessionAgentId(session.AgentId)!.Value;
            var (items, usage, sent) = SessionEvents.Read(envelope.Events, estimator);
            return Outcome<ConversationView>.Ok(new ConversationView(session.Id, OwnConversation.Title(session), agent.Namespace, agent.Name, items, usage, sent));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Reading conversation {ConversationId} failed for {UserId}", query.ConversationId, query.UserId);
            return Outcome<ConversationView>.Unavailable(ChatFailure.Message(ex));
        }
    }
}
