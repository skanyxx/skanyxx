using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Access;

/// <summary>
/// Who may read, search, write and lift where.
/// Humans: company and their own personal scope; the teams they are members of and those teams' departments (the org
/// tree, D055, asked live through <see cref="IOrgMembership"/>). Owner and supervisors also read and search every team
/// and department scope — oversight — but write, like anyone, only where they are members; company writes stay a
/// supervisor's. A team/department slug with no org object behind it has no members, so only owner/supervisor read it.
/// Agents: their grants (D033), or the default when they have none: search company + the caller's personal, upsert
/// only the caller's personal (D040, D052, D078). An agent acting for a user never gets that user's teams: grants are
/// the agent boundary.
/// </summary>
public sealed class AccessPolicy(MemoryDbContext db, IOrgMembership org)
{
    private (string UserId, Task<OrgMembership> Membership)? _membership;

    /// <summary>From the signed-in user's role (owner or supervisor); an agent never is one.</summary>
    public bool IsSupervisor(MemoryCaller caller) => !caller.IsAgent && caller.IsSupervisor;

    /// <summary>The owner role only; an agent never is one.</summary>
    public bool IsOwner(MemoryCaller caller) => !caller.IsAgent && caller.IsOwner;

    /// <summary>
    /// Reading one scope (library GET, lift source, the current card in a 409). An agent reads only what its grants let it
    /// search: it never reaches membership, even when it acts for a user who is a member (D4 of D090).
    /// </summary>
    public async Task<bool> CanReadAsync(MemoryCaller caller, Scope scope, CancellationToken ct)
    {
        if (caller.IsAgent)
            return (await SearchScopesAsync(caller, ct)).Includes(scope);

        return scope.Level switch
        {
            ScopeLevel.Personal => scope == caller.PersonalScope,
            ScopeLevel.Company => true,
            _ => IsSupervisor(caller) || await IsMemberAsync(caller, scope, ct)
        };
    }

    /// <summary>
    /// D103 (on top of <see cref="CanReadAsync"/>): a company card that is not published is a draft, shown only to those
    /// who may write company (supervisors, the owner). `who` names the last writer, not an author, so it grants nothing. To
    /// anyone else it does not exist — callers answer
    /// exactly as for a missing card, so a guessed key reveals nothing.
    /// </summary>
    public bool CanOpen(MemoryCaller caller, Card card) =>
        card.Status == CardStatus.Published
        || Scope.Parse(card.Scope).Level != ScopeLevel.Company
        || IsSupervisor(caller);

    public async Task<SearchScopes> SearchScopesAsync(MemoryCaller caller, CancellationToken ct)
    {
        if (!caller.IsAgent)
        {
            List<string> scopes = [Scope.CompanyName];
            if (caller.PersonalScope is { } own)
                scopes.Add(own.ToString());
            // Oversight already covers every team and department scope, so a supervisor's membership adds nothing.
            if (IsSupervisor(caller))
                return new SearchScopes(scopes, AllTeamsAndDepartments: true);

            var membership = await MembershipAsync(caller, ct);
            scopes.AddRange(membership.Teams.Select(t => new Scope(ScopeLevel.Team, t).ToString()));
            scopes.AddRange(membership.Departments.Select(d => new Scope(ScopeLevel.Department, d).ToString()));
            return new SearchScopes(scopes, AllTeamsAndDepartments: false);
        }

        var grants = await GrantsAsync(caller, ct);
        return new SearchScopes(
            grants.Where(g => g.CanSearch).Select(g => Resolve(g.Scope, caller)).OfType<string>().ToList(),
            AllTeamsAndDepartments: false);
    }

    /// <summary>Writing a card there, or lifting one into it.</summary>
    public async Task<bool> CanUpsertAsync(MemoryCaller caller, Scope scope, CancellationToken ct)
    {
        if (!caller.IsAgent)
            return scope.Level switch
            {
                ScopeLevel.Personal => scope == caller.PersonalScope,
                ScopeLevel.Company => IsSupervisor(caller),
                _ => await IsMemberAsync(caller, scope, ct)
            };

        var target = scope.ToString();
        var grants = await GrantsAsync(caller, ct);
        return grants.Any(g => g.CanUpsert && Resolve(g.Scope, caller) == target);
    }

    /// <summary>
    /// Every scope a person may write, i.e. upsert, lift into or rename in (the library offers only these): their
    /// personal scope, their teams and departments, and company. Each candidate goes through <see cref="CanUpsertAsync"/>,
    /// so this list cannot drift from the rule it enumerates. A person's only; an agent's writes are its grants.
    /// </summary>
    public async Task<IReadOnlyList<Scope>> WritableScopesAsync(MemoryCaller caller, CancellationToken ct)
    {
        if (caller.IsAgent)
            throw new InvalidOperationException("Writable scopes are a person's; an agent's are its grants.");

        var membership = await MembershipAsync(caller, ct);
        List<Scope> candidates =
        [
            caller.PersonalScope!.Value,
            .. membership.Teams.Order().Select(t => new Scope(ScopeLevel.Team, t)),
            .. membership.Departments.Order().Select(d => new Scope(ScopeLevel.Department, d)),
            Scope.Company
        ];
        var writable = new List<Scope>();
        foreach (var scope in candidates)
            if (await CanUpsertAsync(caller, scope, ct))
                writable.Add(scope);
        return writable;
    }

    /// <summary>Why <see cref="CanUpsertAsync"/> said no, in the caller's terms: an agent lacks a grant, a person lacks a right.</summary>
    public static string UpsertRefusal(MemoryCaller caller, Scope scope) => caller.IsAgent
        ? $"No upsert grant on '{scope}'."
        : scope.Level switch
        {
            ScopeLevel.Personal => $"Only its owner may write to '{scope}'.",
            ScopeLevel.Company => "Only a supervisor may write to 'company'.",
            _ => $"Only members of {scope} may write there."
        };

    /// <summary>A person in the team, or in a team of the department. An agent is never a member of anything.</summary>
    public async Task<bool> IsMemberAsync(MemoryCaller caller, Scope scope, CancellationToken ct)
    {
        if (caller.IsAgent || scope.Level is not (ScopeLevel.Team or ScopeLevel.Department))
            return false;

        var membership = await MembershipAsync(caller, ct);
        return scope.Level == ScopeLevel.Team ? membership.Teams.Contains(scope.Id!) : membership.Departments.Contains(scope.Id!);
    }

    /// <summary>
    /// Asked once per request (this policy is scoped) and never kept longer, so a membership change counts from the next
    /// request. Keyed by user, so a scope that ever serves two callers cannot hand one the other's teams.
    /// </summary>
    private Task<OrgMembership> MembershipAsync(MemoryCaller caller, CancellationToken ct)
    {
        var userId = caller.UserId!;
        if (_membership is not { } cached || cached.UserId != userId)
            _membership = cached = (userId, org.ForUserAsync(userId, ct));
        return cached.Membership;
    }

    private async Task<List<AgentGrant>> GrantsAsync(MemoryCaller caller, CancellationToken ct)
    {
        var grants = await db.Grants.AsNoTracking().Where(g => g.AgentId == caller.AgentId).ToListAsync(ct);
        return grants.Count > 0 ? grants : DefaultGrants(caller.AgentId!);
    }

    private static List<AgentGrant> DefaultGrants(string agentId) =>
    [
        new() { AgentId = agentId, Scope = Scope.CompanyName, CanSearch = true },
        new() { AgentId = agentId, Scope = AgentGrant.CallerPersonal, CanSearch = true, CanUpsert = true }
    ];

    private static string? Resolve(string grantScope, MemoryCaller caller) =>
        grantScope == AgentGrant.CallerPersonal ? caller.PersonalScope?.ToString() : grantScope;
}
