using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Agents;

internal sealed class ChatAgentDetailsHandler(KAgentApiClient kagent, IAgentMemoryAccess memory, ILogger<ChatAgentDetailsHandler> logger)
    : IRequestHandler<ChatAgentDetailsQuery, Outcome<ChatAgentDetails>>
{
    public async Task<Outcome<ChatAgentDetails>> Handle(ChatAgentDetailsQuery query, CancellationToken ct)
    {
        try
        {
            if (await MergedAgents.FindAsync(kagent, query.Namespace, query.Name, ct) is not { } agent)
                return Outcome<ChatAgentDetails>.NotFound("No such agent to chat with.");
            var entries = kagent.GetAgentListAsync(ct);
            var servers = kagent.GetToolServerListAsync(ct);
            await Task.WhenAll(entries, servers);
            // An agent's memory id is its kagent name (the studio grants by it).
            return Outcome<ChatAgentDetails>.Ok(AgentDetailsReader.Read(
                agent, AgentEntries.Find(entries.Result, agent.Namespace, agent.Name), servers.Result, await memory.ForAgentAsync(agent.Name, ct)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent details {Namespace}/{Name}: kagent did not answer", query.Namespace, query.Name);
            return Outcome<ChatAgentDetails>.Unavailable(ChatFailure.Message(ex));
        }
    }
}
