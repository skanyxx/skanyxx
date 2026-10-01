using System.Security.Claims;
using System.Text.Json;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>The Entra id-token claims the mapping reads, under their own names (the handler runs with <c>MapInboundClaims = false</c>).</summary>
internal static class EntraClaims
{
    public const string TenantId = "tid";
    public const string ObjectId = "oid";
    public const string Groups = "groups";
    public const string AccountType = "acct";
    public const string Email = "email";
    public const string PreferredUserName = "preferred_username";
    public const string Name = "name";
    public const string IdentityProvider = "idp";
    public const string EmailDomainOwnerVerified = "xms_edov";

    private const string ClaimNames = "_claim_names";
    private const string HasGroups = "hasgroups";

    /// <summary>The account key: <c>tid|oid</c>, both lowercase GUIDs, the pair Microsoft names as a user's stable id.</summary>
    public static string Key(Guid tenantId, Guid objectId) => $"{tenantId:D}|{objectId:D}";

    /// <summary>
    /// <c>acct = 1</c> is a guest (B2B), <c>0</c> a member. An <c>idp</c> claim that is not exactly one of this tenant's
    /// issuer forms (<see cref="IsThisTenant"/>) — a user homed in another tenant, a personal Microsoft account, a
    /// federated partner — is a guest whatever <c>acct</c> says. No <c>acct</c> at all is
    /// <see cref="EntraAccountKind.Unknown"/> (D9).
    /// </summary>
    public static EntraAccountKind AccountKind(ClaimsPrincipal principal, string tenantId)
    {
        var acct = principal.FindFirstValue(AccountType);
        if (acct == "1" || (principal.FindFirstValue(IdentityProvider) is { } idp && !IsThisTenant(idp, tenantId)))
            return EntraAccountKind.Guest;
        return acct == "0" ? EntraAccountKind.Member : EntraAccountKind.Unknown;
    }

    /// <summary>
    /// SEC N5: the v1 (<c>https://sts.windows.net/{tid}/</c>) or v2 (<c>https://login.microsoftonline.com/{tid}/v2.0</c>)
    /// issuer of <paramref name="tenantId"/>, compared whole (case aside) — never a substring, which a federated
    /// partner's issuer could satisfy by embedding the tenant id.
    /// </summary>
    public static bool IsThisTenant(string idp, string tenantId) =>
        idp.Equals($"https://sts.windows.net/{tenantId}/", StringComparison.OrdinalIgnoreCase)
        || idp.Equals($"https://login.microsoftonline.com/{tenantId}/v2.0", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>xms_edov</c> present and false: the tenant does not vouch for the email's domain (SEC L3).</summary>
    public static bool EmailUnverified(ClaimsPrincipal principal) =>
        principal.FindFirstValue(EmailDomainOwnerVerified) is { } verified && (verified == "0" || verified.Equals("false", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Group overage: the token says the groups are elsewhere (<c>_claim_names</c> names <c>groups</c>, or the implicit
    /// flow's <c>hasgroups</c>). Only the presence counts; the <c>_claim_sources</c> endpoint is never followed (it may
    /// point at the retired AAD Graph). An unreadable <c>_claim_names</c> counts as overage: Graph is the authority.
    /// </summary>
    public static bool IsOverage(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == HasGroups))
            return true;
        if (principal.FindFirstValue(ClaimNames) is not { } names)
            return false;
        try
        {
            using var json = JsonDocument.Parse(names);
            return json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.TryGetProperty(Groups, out _);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>An Entra id (tenant, client, group) as Skanyxx stores it: a lowercase GUID; null for anything that is not a GUID.</summary>
    public static string? NormalizeId(string? value) => Guid.TryParse(value, out var id) ? id.ToString("D") : null;
}
