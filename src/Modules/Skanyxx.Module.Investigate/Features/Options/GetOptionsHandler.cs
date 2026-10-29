using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Contracts;

namespace Skanyxx.Module.Investigate.Features.Options;

/// <summary>The editor's choices. kagent being down is said (<c>agentsAvailable: false</c>), so the page can explain it.</summary>
internal sealed class GetOptionsHandler(IInvestigationAgents agents, OrgTeams org, IOptions<InvestigateOptions> options)
    : IRequestHandler<GetOptionsQuery, Outcome<OptionsDto>>
{
    public async Task<Outcome<OptionsDto>> Handle(GetOptionsQuery query, CancellationToken ct)
    {
        IReadOnlyList<AgentChoice> list = [];
        var available = true;
        try
        {
            list = await agents.ListAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            available = false;
        }

        var o = options.Value;
        return Outcome<OptionsDto>.Ok(new OptionsDto(list, available, await org.ShareableAsync(query.Viewer, ct),
            new LimitsDto(o.MaxDepth, o.MaxSteps, o.MaxPromptChars, o.MaxInputChars, o.StepTimeoutSeconds)));
    }
}
