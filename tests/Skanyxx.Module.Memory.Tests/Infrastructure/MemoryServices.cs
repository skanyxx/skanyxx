using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>The Core interfaces other modules call (Chat, chatcontext, Tickets), each in its own scope like a request.</summary>
public static class MemoryServices
{
    public static async Task<MemoryCardRef?> ChatTurnAsync(this MemoryApp app, ChatTurnDigest turn)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICardExtractor>().ChatTurnAsync(turn, CancellationToken.None);
    }

    public static async Task<MemoryCardRef?> TicketRunAsync(this MemoryApp app, TicketRunFinished run)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICardExtractor>().TicketRunAsync(run, CancellationToken.None);
    }

    public static async Task<MemoryContext> RelatedAsync(this MemoryApp app, string userId, string agentId, string text, params string[] tickets)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRelatedMemory>()
            .ForNewChatAsync(new RelatedMemoryQuery(userId, agentId, text, tickets), CancellationToken.None);
    }

    public static Task<HttpResponseMessage> PutLinkedCardAsync(
        this HttpClient client, string scope, string key, string what, string why, string[]? tickets = null, string[]? links = null,
        int version = 0, string type = "decision") =>
        System.Net.Http.Json.HttpClientJsonExtensions.PutAsJsonAsync(
            client, $"/api/memory/cards/{scope}/{key}", new { version, type, what, why, tickets, links });
}
