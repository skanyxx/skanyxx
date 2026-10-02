namespace Skanyxx.Module.Identity.Accounts;

/// <summary>What a refresh token names: its chain, its own id within it, and the chain's absolute expiry.</summary>
internal sealed record RefreshChain(string Id, string TokenId, DateTimeOffset ExpiresUtc);
