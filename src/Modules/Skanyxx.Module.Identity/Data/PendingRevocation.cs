namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// D13: a <c>PrivilegesRevoked</c> the account is owed. Written in the transaction that took supervisor away (or
/// disabled the account) and deleted once the publish after the commit succeeded, so a failed publish is retried by the
/// next save or Microsoft sign-in of that account. Its own table, not a column on <c>identity_users</c>: Identity
/// rewrites the whole user row on every update, and a request holding an older copy would write a cleared flag back.
/// <see cref="Generation"/> changes on every mark, so settling one publish never clears a mark made after it.
/// </summary>
public sealed class PendingRevocation
{
    public string UserId { get; set; } = "";
    public Guid Generation { get; set; }
    public DateTimeOffset MarkedUtc { get; set; }
}
