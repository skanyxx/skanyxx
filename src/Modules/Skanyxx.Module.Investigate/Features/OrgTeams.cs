using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features;

/// <summary>The org tree as Investigate needs it, read live from identity (D055): no cache, so a removal counts at once.</summary>
internal sealed class OrgTeams(IOrgMembership membership, IMediator mediator)
{
    /// <summary>
    /// The viewer's <see cref="Reach"/> (D371), the way Analytics scopes a supervisor: the teams they are a member of and
    /// the people in them. If identity cannot list the teams, a supervisor reaches only their own work.
    /// </summary>
    public async Task<Reach> ReachAsync(Viewer viewer, CancellationToken ct)
    {
        if (viewer.IsOwner)
            return Reach.None;
        if (!viewer.IsSupervisor)
            return new Reach((await membership.ForUserAsync(viewer.UserId, ct)).Teams, new HashSet<string>());

        var teams = await mediator.Send(new ListTeamsQuery(), ct);
        if (teams.Status != OutcomeStatus.Ok)
            return Reach.None;
        var mine = teams.Value!.Where(t => t.Members.Contains(viewer.UserId)).ToList();
        return new Reach(mine.Select(t => t.Slug).ToHashSet(), mine.SelectMany(t => t.Members).ToHashSet());
    }

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
