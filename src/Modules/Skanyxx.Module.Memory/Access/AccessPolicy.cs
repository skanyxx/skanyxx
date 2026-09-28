using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Access;

/// <summary>
/// Who may read, search, write and lift where. Humans: company + their own personal.
/// Agents: their grants (D033), or the default when they have none: search company + the caller's personal,
/// upsert only the caller's personal (D040, D052).
/// TODO(identity-slice): team/department membership is not checked until the org tree exists (D055).
/// </summary>
public sealed class AccessPolicy(MemoryDbContext db)
{
    /// <summary>From the signed-in user's role (owner or supervisor); an agent never is one.</summary>
    public bool IsSupervisor(MemoryCaller caller) => !caller.IsAgent && caller.IsSupervisor;

    /// <summary>The owner role only; an agent never is one.</summary>
    public bool IsOwner(MemoryCaller caller) => !caller.IsAgent && caller.IsOwner;

    public bool CanRead(MemoryCaller caller, Scope scope) => scope.Level switch
    {
        ScopeLevel.Personal => scope == caller.PersonalScope,
        _ => true
    };

    /// <summary>Whether a card in this scope may be shown to the caller (e.g. as the current state in a 409).</summary>
    public async Task<bool> CanSeeAsync(MemoryCaller caller, Scope scope, CancellationToken ct) =>
        caller.IsAgent
            ? (await SearchScopesAsync(caller, ct)).Contains(scope.ToString())
            : CanRead(caller, scope);

    public async Task<IReadOnlyList<string>> SearchScopesAsync(MemoryCaller caller, CancellationToken ct)
    {
        if (!caller.IsAgent)
            return caller.PersonalScope is { } own ? [Scope.CompanyName, own.ToString()] : [Scope.CompanyName];

        var grants = await GrantsAsync(caller, ct);
        return grants.Where(g => g.CanSearch)
            .Select(g => Resolve(g.Scope, caller))
            .OfType<string>()
            .ToList();
    }

    public async Task<bool> CanUpsertAsync(MemoryCaller caller, Scope scope, CancellationToken ct)
    {
        if (!caller.IsAgent)
            return scope.Level switch
            {
                ScopeLevel.Personal => scope == caller.PersonalScope,
                ScopeLevel.Company => IsSupervisor(caller),
                _ => true
            };

        var target = scope.ToString();
        var grants = await GrantsAsync(caller, ct);
        return grants.Any(g => g.CanUpsert && Resolve(g.Scope, caller) == target);
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
