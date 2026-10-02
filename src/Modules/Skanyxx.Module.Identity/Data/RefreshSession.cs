namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// One bearer sign-in's refresh-token chain. Only the newest token of a chain (<see cref="TokenId"/>) is honoured;
/// presenting an older one revokes the chain. <see cref="ExpiresUtc"/> is the absolute session cap from the sign-in.
/// </summary>
public sealed class RefreshSession
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string TokenId { get; set; } = "";
    public DateTimeOffset ExpiresUtc { get; set; }
}
