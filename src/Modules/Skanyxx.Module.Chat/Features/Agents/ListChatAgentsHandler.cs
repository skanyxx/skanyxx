using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>
/// The picker: only the agents Chat offers (<see cref="MergedAgents"/>), each with the model kagent reports and whether
/// its tool servers look fine (D554).
/// </summary>
internal sealed class ListChatAgentsHandler(KAgentApiClient kagent, ILogger<ListChatAgentsHandler> logger)
    : IRequestHandler<ListChatAgentsQuery, Outcome<IReadOnlyList<ChatAgent>>>
{
    public async Task<Outcome<IReadOnlyList<ChatAgent>>> Handle(ListChatAgentsQuery query, CancellationToken ct)
    {
        try
        {
            var agents = MergedAgents.ListAsync(kagent, ct);
            var entries = kagent.GetAgentListAsync(ct);
            var servers = ToolServerHealth.ListOrNullAsync(kagent, logger, ct);
            await Task.WhenAll(agents, entries, servers);
            return Outcome<IReadOnlyList<ChatAgent>>.Ok([.. agents.Result
                .OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Namespace, StringComparer.Ordinal)
                .Select(a => new ChatAgent(a.Name, a.Namespace, a.Description, a.Ready,
                    AgentEntries.Model(AgentEntries.Find(entries.Result, a.Namespace, a.Name)), MergedAgents.AcceptsImages(a),
                    servers.Result is { } list ? ToolServerHealth.Summary(ToolServerHealth.Problems(a.McpServerRefs, list), query.Internals) : null))]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Listing chat agents failed");
            return Outcome<IReadOnlyList<ChatAgent>>.Unavailable(ChatFailure.Message(ex));
        }
    }
}
