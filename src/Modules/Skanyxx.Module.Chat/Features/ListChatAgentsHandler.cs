using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features;

internal sealed class ListChatAgentsHandler(KAgentApiClient kagent, ILogger<ListChatAgentsHandler> logger)
    : IRequestHandler<ListChatAgentsQuery, Outcome<IReadOnlyList<ChatAgent>>>
{
    public async Task<Outcome<IReadOnlyList<ChatAgent>>> Handle(ListChatAgentsQuery query, CancellationToken ct)
    {
        try
        {
            var agents = await MergedAgents.ListAsync(kagent, ct);
            return Outcome<IReadOnlyList<ChatAgent>>.Ok([.. agents.Select(a => new ChatAgent(a.Name, a.Namespace, a.Description, a.Ready))]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Listing chat agents failed");
            return Outcome<IReadOnlyList<ChatAgent>>.Unavailable(ChatFailure.Generic);
        }
    }
}
