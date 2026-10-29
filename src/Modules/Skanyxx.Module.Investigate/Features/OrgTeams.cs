using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features;

/// <summary>
/// The org tree as Investigate needs it, read live from identity (D055). Scoped, and read at most once per request
/// (D386): a handler asking for reach and then for shareable teams lists the teams once, and a removal still counts on
/// the next request.
/// </summary>
internal sealed class OrgTeams(IOrgMembership membership, IMediator mediator)
{
    private (Viewer Viewer, Reach Reach)? _reach;
    private Outcome<IReadOnlyList<TeamDto>>? _allTeams;

    /// <summary>
    /// The viewer's <see cref="Reach"/> (D371). Everyone reaches the workflows shared to their own teams. A supervisor
    /// also reaches the people in those teams, except the owner and other supervisors (D386): a peer's or the owner's
    /// inputs and answers are not theirs to read, nor their workflows to edit. If identity cannot list the teams or the
    /// people, a supervisor keeps their team shares, as a builder does, and reaches no one else's work.
    /// </summary>
    public async Task<Reach> ReachAsync(Viewer viewer, CancellationToken ct)
    {
        if (_reach is { } cached && cached.Viewer == viewer)
            return cached.Reach;
        var reach = await ReadReachAsync(viewer, ct);
        _reach = (viewer, reach);
        return reach;
    }

    private async Task<Reach> ReadReachAsync(Viewer viewer, CancellationToken ct)
    {
        if (viewer.IsOwner)
            return Reach.None;
        var teams = (await membership.ForUserAsync(viewer.UserId, ct)).Teams;
        if (!viewer.IsSupervisor)
            return new Reach(teams, new HashSet<string>());

        var all = await AllTeamsAsync(ct);
        var people = await mediator.Send(new ListPeopleQuery(), ct);
        if (all.Status != OutcomeStatus.Ok || people.Status != OutcomeStatus.Ok)
            return new Reach(teams, new HashSet<string>());
        var senior = people.Value!.Where(p => p.Roles.Contains(SkanyxxRoles.Owner) || p.Roles.Contains(SkanyxxRoles.Supervisor))
            .Select(p => p.Id).ToHashSet();
        var reached = all.Value!.Where(t => teams.Contains(t.Slug)).SelectMany(t => t.Members).Where(m => !senior.Contains(m)).ToHashSet();
        return new Reach(teams, reached);
    }

    /// <summary>Who a workflow may be shared with: any team for the owner and supervisors, a builder's own teams otherwise.</summary>
    public async Task<IReadOnlyList<TeamChoice>> ShareableAsync(Viewer viewer, CancellationToken ct)
    {
        var teams = viewer.IsSupervisor
            ? await AllTeamsAsync(ct)
            : await mediator.Send(new PersonTeamsQuery(viewer.UserId), ct);
        return teams.Status == OutcomeStatus.Ok
            ? [.. teams.Value!.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => new TeamChoice(t.Slug, t.Name))]
            : [];
    }

    private async Task<Outcome<IReadOnlyList<TeamDto>>> AllTeamsAsync(CancellationToken ct) =>
        _allTeams ??= await mediator.Send(new ListTeamsQuery(), ct);
}
