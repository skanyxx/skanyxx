using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>Identity's org tree for tests: billing (bea, sam), sales (ben); people carry the roles <see cref="InvestigateApp"/> signs them in with.</summary>
public sealed class FakeOrg : IOrgMembership,
    IRequestHandler<ListTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>,
    IRequestHandler<PersonTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>,
    IRequestHandler<ListPeopleQuery, Outcome<IReadOnlyList<PersonDto>>>
{
    public List<TeamDto> Teams { get; } =
    [
        new("billing", "Billing", "finance", ["bea", "sam"]),
        new("sales", "Sales", "sales", ["ben"]),
    ];

    /// <summary>When false, listing teams fails, as identity would if its database were down.</summary>
    public bool TeamsListable { get; set; } = true;

    public Task<OrgMembership> ForUserAsync(string userId, CancellationToken ct)
    {
        var teams = Teams.Where(t => t.Members.Contains(userId)).ToList();
        return Task.FromResult(new OrgMembership(teams.Select(t => t.Slug).ToHashSet(), teams.Select(t => t.Department).ToHashSet()));
    }

    public Task<Outcome<IReadOnlyList<TeamDto>>> Handle(ListTeamsQuery request, CancellationToken ct) =>
        Task.FromResult(TeamsListable ? Outcome<IReadOnlyList<TeamDto>>.Ok([.. Teams]) : Outcome<IReadOnlyList<TeamDto>>.Unavailable("identity is down"));

    public Task<Outcome<IReadOnlyList<TeamDto>>> Handle(PersonTeamsQuery request, CancellationToken ct) =>
        Task.FromResult(Outcome<IReadOnlyList<TeamDto>>.Ok([.. Teams.Where(t => t.Members.Contains(request.UserId))]));

    public Task<Outcome<IReadOnlyList<PersonDto>>> Handle(ListPeopleQuery request, CancellationToken ct) =>
        Task.FromResult(Outcome<IReadOnlyList<PersonDto>>.Ok(
            [.. InvestigateApp.RolesOf.Select(p => new PersonDto(p.Key, $"{p.Key}@acme.test", p.Key, [p.Value], false, false, true))]));
}
