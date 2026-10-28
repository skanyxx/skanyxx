using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features;

/// <summary>
/// The Chat page's reload (D266, D274): kagent is asked as the person, and the session must say it is theirs, so anyone
/// else's conversation (the owner's included) is "not found".
/// </summary>
internal sealed class ConversationEventsHandler(KAgentApiClient kagent, ILogger<ConversationEventsHandler> logger)
    : IRequestHandler<ConversationEventsQuery, Outcome<IReadOnlyList<JsonElement>>>
{
    public async Task<Outcome<IReadOnlyList<JsonElement>>> Handle(ConversationEventsQuery query, CancellationToken ct)
    {
        try
        {
            var envelope = await kagent.GetSessionWithEventsAsync(query.ConversationId, query.UserId, ct);
            return envelope?.Session?.UserId == query.UserId
                ? Outcome<IReadOnlyList<JsonElement>>.Ok(envelope.Events)
                : Outcome<IReadOnlyList<JsonElement>>.NotFound("No such conversation.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Reading conversation {ConversationId} failed for {UserId}", query.ConversationId, query.UserId);
            return Outcome<IReadOnlyList<JsonElement>>.Unavailable(ChatFailure.Generic);
        }
    }
}
