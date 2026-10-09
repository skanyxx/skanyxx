using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class EditDraftHandler(StudioReconciler reconciler, IOptions<StudioOptions> options, ILogger<EditDraftHandler> logger)
    : IRequestHandler<EditDraftQuery, Outcome<AgentDraft>>
{
    public async Task<Outcome<AgentDraft>> Handle(EditDraftQuery query, CancellationToken ct)
    {
        if (!query.User.CanPropose)
            return Outcome<AgentDraft>.Forbidden(StudioFailure.NotBuilder);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<AgentDraft>(StudioFailure.NotConfigured);
        try
        {
            var (draft, _) = await reconciler.ReadAgentAsync(query.Agent, options.Value.Git.Branch, ct);
            return draft is null ? Outcome<AgentDraft>.NotFound($"No valid agent '{query.Agent}' in main.") : Outcome<AgentDraft>.Ok(draft);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio could not read {Agent} from main", query.Agent);
            return Outcome<AgentDraft>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
