using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Chat.Features.Agents;

namespace Skanyxx.Module.Chat.Features.Conversations;

internal sealed class ListConversationsHandler(KAgentApiClient kagent, ILogger<ListConversationsHandler> logger)
    : IRequestHandler<ListConversationsQuery, Outcome<IReadOnlyList<ConversationSummary>>>
{
    public async Task<Outcome<IReadOnlyList<ConversationSummary>>> Handle(ListConversationsQuery query, CancellationToken ct)
    {
        try
        {
            return Outcome<IReadOnlyList<ConversationSummary>>.Ok([.. (await kagent.ListSessionsAsync(query.UserId, ct))
                .Where(s => OwnConversation.IsOwn(s, query.UserId))
                .Select(s => (Session: s, Agent: OfferedAgents.FromSessionAgentId(s.AgentId)!.Value))
                .Select(s => new ConversationSummary(s.Session.Id, OwnConversation.Title(s.Session), s.Agent.Namespace, s.Agent.Name,
                    s.Session.CreatedAt, s.Session.LastUpdateTime ?? s.Session.CreatedAt))
                .OrderByDescending(s => EventFields.Time(System.Text.Json.Nodes.JsonValue.Create(s.UpdatedAt)) ?? DateTimeOffset.MinValue)]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Listing conversations failed for {UserId}", query.UserId);
            return Outcome<IReadOnlyList<ConversationSummary>>.Unavailable(ChatFailure.Message(ex));
        }
    }
}
