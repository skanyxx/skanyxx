using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Agents;

internal sealed class ChatAgentDetailsHandler(KAgentApiClient kagent, IAgentMemoryAccess memory, CostCalculator cost, ILogger<ChatAgentDetailsHandler> logger)
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
            // An agent's memory id is its kagent name (the studio grants by it). The price key is the quota's and the rollup's.
            var model = ChatQuotaUsage.ModelRef(agent.Namespace, agent.ModelConfig);
            var details = AgentDetailsReader.Read(
                agent, AgentEntries.Find(entries.Result, agent.Namespace, agent.Name), servers.Result, await memory.ForAgentAsync(agent.Name, ct), query.Internals);
            return Outcome<ChatAgentDetails>.Ok(details with
            {
                Price = cost.PriceOf(model) is { } price ? new ChatAgentPrice(model, price.InputPerMillion, price.OutputPerMillion, cost.Currency) : null
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent details {Namespace}/{Name}: kagent did not answer", query.Namespace, query.Name);
            return Outcome<ChatAgentDetails>.Unavailable(ChatFailure.Message(ex));
        }
    }
}
