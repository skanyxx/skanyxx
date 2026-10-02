namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// Microsoft's endpoints for a single tenant (global cloud). Virtual so tests can point them at a mock identity
/// provider; production never overrides them.
/// </summary>
internal class EntraEndpoints
{
    /// <summary>Single-tenant v2.0 authority: the discovery document's issuer names this tenant, so the handler checks it.</summary>
    public virtual string Authority(string tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";

    public virtual Uri Token(string tenantId) => new($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token");

    public virtual Uri CheckMemberGroups(string objectId) => new($"https://graph.microsoft.com/v1.0/users/{objectId}/checkMemberGroups");

    public const string GraphScope = "https://graph.microsoft.com/.default";
}
