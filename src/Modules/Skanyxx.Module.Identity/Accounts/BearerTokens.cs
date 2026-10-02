using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Issues and reads the tokens of Identity's bearer scheme with that scheme's own protectors and lifetimes, so the
/// Host's bearer handler accepts what is issued here. Same ticket shape as <c>MapIdentityApi</c>; access and refresh
/// tokens use different protectors, so one can never stand in for the other. A refresh token also names its
/// <see cref="RefreshChain"/>; neither token outlives the chain's cap.
/// </summary>
internal sealed class BearerTokens(IOptionsMonitor<BearerTokenOptions> monitor, TimeProvider time)
{
    private const string ChainKey = "skanyxx.chain";
    private const string TokenKey = "skanyxx.token";
    private const string ChainExpiresKey = "skanyxx.chain_expires";

    private BearerTokenOptions Options => monitor.Get(IdentityConstants.BearerScheme);

    public AccessTokenResponse Issue(ClaimsPrincipal principal, RefreshChain chain)
    {
        var options = Options;
        var now = time.GetUtcNow();
        // Capped like the refresh token: one issued near the chain's end must not outlive the session (SEC2-N4).
        var accessExpires = Min(now + options.BearerTokenExpiration, chain.ExpiresUtc);
        var access = new AuthenticationTicket(principal,
            new AuthenticationProperties { ExpiresUtc = accessExpires }, IdentityConstants.BearerScheme);
        var refreshProperties = new AuthenticationProperties
        {
            ExpiresUtc = Min(now + options.RefreshTokenExpiration, chain.ExpiresUtc),
            Items =
            {
                [ChainKey] = chain.Id,
                [TokenKey] = chain.TokenId,
                [ChainExpiresKey] = chain.ExpiresUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            }
        };
        var refresh = new AuthenticationTicket(principal, refreshProperties, $"{IdentityConstants.BearerScheme}:RefreshToken");
        return new AccessTokenResponse
        {
            AccessToken = options.BearerTokenProtector.Protect(access),
            ExpiresIn = (long)(accessExpires - now).TotalSeconds,
            RefreshToken = options.RefreshTokenProtector.Protect(refresh)
        };
    }

    /// <summary>The principal and chain inside an unexpired refresh token, or null (tampered, expired, not a refresh token).</summary>
    public (ClaimsPrincipal Principal, RefreshChain Chain)? ReadRefresh(string refreshToken)
    {
        var ticket = Options.RefreshTokenProtector.Unprotect(refreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || time.GetUtcNow() >= expires)
            return null;

        var items = ticket.Properties.Items;
        if (!items.TryGetValue(ChainKey, out var chain) || !items.TryGetValue(TokenKey, out var token)
            || !items.TryGetValue(ChainExpiresKey, out var chainExpires) || chain is null || token is null)
            return null;
        return (ticket.Principal, new RefreshChain(chain, token,
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(chainExpires!, CultureInfo.InvariantCulture))));
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
