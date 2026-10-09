using System.Security.Claims;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// Group ids → roles and teams (the union over every mapped group the person is in). Groups come from the token's
/// <c>groups</c> claim, or from Graph when the token reports an overage — asking only about the mapped ids. Guests, and
/// tokens that cannot tell (no <c>acct</c>), stop here, before Graph is asked anything. Only <see cref="SkanyxxRoles.Grantable"/> roles ever come out: a mapping never
/// makes an owner, whatever was stored.
/// </summary>
internal sealed class EntraMapper(IGraphMembership graph)
{
    public async Task<EntraMapping> MapAsync(ClaimsPrincipal principal, EntraConfig config, CancellationToken ct)
    {
        var kind = EntraClaims.AccountKind(principal, config.TenantId);
        if (kind != EntraAccountKind.Member)
            return new EntraMapping(kind, 0, [], []);

        IReadOnlySet<string> groups = EntraClaims.IsOverage(principal)
            ? await graph.MemberOfAsync(config, Guid.Parse(principal.FindFirstValue(EntraClaims.ObjectId)!), [.. config.Groups.Keys], ct)
            : principal.FindAll(EntraClaims.Groups).Select(c => EntraClaims.NormalizeId(c.Value)).OfType<string>().ToHashSet();
        return Map(kind, groups, config);
    }

    /// <summary>
    /// D158: the mapping for a member by what Graph says now (no token, so no guest check: guests are refused at sign-in
    /// and an account is only ever created or linked for a member).
    /// </summary>
    public async Task<EntraMapping> RecheckAsync(Guid objectId, EntraConfig config, CancellationToken ct) =>
        Map(EntraAccountKind.Member, await graph.MemberOfAsync(config, objectId, [.. config.Groups.Keys], ct), config);

    private static EntraMapping Map(EntraAccountKind kind, IReadOnlySet<string> groups, EntraConfig config)
    {
        var rules = groups.Where(config.Groups.ContainsKey).Select(g => config.Groups[g]).ToList();
        return new EntraMapping(kind, rules.Count,
            [.. rules.SelectMany(r => r.Roles).Where(SkanyxxRoles.Grantable.Contains).Distinct().Order()],
            [.. rules.SelectMany(r => r.Teams).Distinct().Order()]);
    }
}
