using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>Identity's org tree for tests: billing (bea, sam), sales (ben).</summary>
public sealed class FakeOrg : IOrgMembership,
    IRequestHandler<ListTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>,
    IRequestHandler<PersonTeamsQuery, Outcome<IReadOnlyList<TeamDto>>>
{
    public List<TeamDto> Teams { get; } =
    [
        new("billing", "Billing", "finance", ["bea", "sam"]),
        new("sales", "Sales", "sales", ["ben"]),
    ];

    public Task<OrgMembership> ForUserAsync(string userId, CancellationToken ct)
    {
        var teams = Teams.Where(t => t.Members.Contains(userId)).ToList();
        return Task.FromResult(new OrgMembership(teams.Select(t => t.Slug).ToHashSet(), teams.Select(t => t.Department).ToHashSet()));
    }

    public Task<Outcome<IReadOnlyList<TeamDto>>> Handle(ListTeamsQuery request, CancellationToken ct) =>
        Task.FromResult(Outcome<IReadOnlyList<TeamDto>>.Ok([.. Teams]));

    public Task<Outcome<IReadOnlyList<TeamDto>>> Handle(PersonTeamsQuery request, CancellationToken ct) =>
        Task.FromResult(Outcome<IReadOnlyList<TeamDto>>.Ok([.. Teams.Where(t => t.Members.Contains(request.UserId))]));
}
