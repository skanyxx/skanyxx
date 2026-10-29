using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features;

/// <summary>The org tree as Investigate needs it, read live from identity (D055): no cache, so a removal counts at once.</summary>
internal sealed class OrgTeams(IOrgMembership membership, IMediator mediator)
{
    public async Task<IReadOnlySet<string>> MemberOfAsync(Viewer viewer, CancellationToken ct) =>
        (await membership.ForUserAsync(viewer.UserId, ct)).Teams;

    /// <summary>Who a workflow may be shared with: any team for the owner and supervisors, a builder's own teams otherwise.</summary>
    public async Task<IReadOnlyList<TeamChoice>> ShareableAsync(Viewer viewer, CancellationToken ct)
    {
        var teams = viewer.IsSupervisor
            ? await mediator.Send(new ListTeamsQuery(), ct)
            : await mediator.Send(new PersonTeamsQuery(viewer.UserId), ct);
        return teams.Status == OutcomeStatus.Ok
            ? [.. teams.Value!.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => new TeamChoice(t.Slug, t.Name))]
            : [];
    }
}
