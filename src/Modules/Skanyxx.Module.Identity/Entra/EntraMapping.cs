namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// What one sign-in's groups map to. <see cref="GrantsAccess"/>: a member (not a guest, nor unknown) in at least one
/// mapped group (D027: unmapped users do not get in, even with nothing to grant).
/// </summary>
internal sealed record EntraMapping(EntraAccountKind Kind, int MappedGroups, IReadOnlyList<string> Roles, IReadOnlyList<string> Teams)
{
    public bool GrantsAccess => Kind == EntraAccountKind.Member && MappedGroups > 0;

    public string RefusalReason => Kind switch
    {
        EntraAccountKind.Guest => "guest account",
        EntraAccountKind.Unknown => "no acct claim in the id token",
        _ => "no mapped group"
    };
}
