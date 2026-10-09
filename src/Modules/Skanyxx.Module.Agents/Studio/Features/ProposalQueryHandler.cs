using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>The supervisor's review: the YAML exactly as it would merge, and what is wrong with it, if anything.</summary>
internal sealed class ProposalQueryHandler(
    IAgentRepo repo, ProposalInspector inspector, KAgentApiClient kagent, IOptions<StudioOptions> options, ILogger<ProposalQueryHandler> logger)
    : IRequestHandler<ProposalQuery, Outcome<ProposalDetail>>
{
    public async Task<Outcome<ProposalDetail>> Handle(ProposalQuery query, CancellationToken ct)
    {
        if (!query.User.CanEnter)
            return Outcome<ProposalDetail>.Forbidden(StudioFailure.NotStudio);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<ProposalDetail>(StudioFailure.NotConfigured);
        try
        {
            if (await repo.PullAsync(query.Number, ct) is not { Open: true } pull)
                return Outcome<ProposalDetail>.NotFound(StudioFailure.NoProposal);
            var inspected = await inspector.InspectAsync(pull, ct);
            PreviewAgent? preview = null;
            if (inspected.Agent is { } agent && StudioNames.IsValidName(agent))
            {
                var name = StudioNames.PreviewAgent(pull.Number, agent);
                preview = (await kagent.GetAgentsAsync(ct))
                    .Where(a => a.Namespace == options.Value.Namespace && a.Name == name && a.Labels.GetValueOrDefault(StudioNames.PreviewLabel) == pull.Number.ToString())
                    .Select(a => new PreviewAgent(a.Namespace, a.Name, a.Ready)).FirstOrDefault();
            }
            return Outcome<ProposalDetail>.Ok(new ProposalDetail(
                new ProposalSummary(pull.Number, pull.Title, inspected.Agent, ProposalTrailer.ProposedBy(pull.Body), pull.HeadSha, pull.Mergeable),
                inspected.Files, inspected.Problems, inspected.NeedsOwner, preview));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio could not read proposal #{Number}", query.Number);
            return Outcome<ProposalDetail>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
