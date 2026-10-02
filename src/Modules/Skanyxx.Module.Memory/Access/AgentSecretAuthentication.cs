using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Access;

/// <summary>
/// D080: the scheme <c>/mcp/memory</c> requires. <c>Authorization: Bearer &lt;agent secret&gt;</c> is looked up by its
/// SHA-256 (unique index), so no plaintext is stored and nothing is compared in code; the principal is the owning agent.
/// Anything else — no header, another scheme, a user's token, an unknown or revoked secret — is a 401 before MCP runs.
/// </summary>
internal sealed class AgentSecretAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, MemoryDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MemoryAgentSecret";

    /// <summary>D084: <c>"true"</c> when the agent may name a user in <c>X-User-Id</c>; otherwise it is ignored.</summary>
    public const string ActsForUsersClaim = "skanyxx:memory:acts_for_users";
    private const string Bearer = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        // Failure messages name the reason, never the value: they reach the authentication log.
        var secret = header[Bearer.Length..].Trim();
        if (!AgentSecretToken.IsWellFormed(secret))
            return AuthenticateResult.Fail("Not an agent secret.");

        var hash = AgentSecretToken.Hash(secret);
        var agent = await db.AgentSecrets.AsNoTracking()
            .Where(s => s.SecretHash == hash)
            .Select(s => new { s.AgentId, s.ActsForUsers })
            .SingleOrDefaultAsync(Context.RequestAborted);
        if (agent is null)
            return AuthenticateResult.Fail("Unknown or revoked agent secret.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, agent.AgentId),
            new Claim(ActsForUsersClaim, agent.ActsForUsers ? "true" : "false", ClaimValueTypes.Boolean)
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem("An agent secret is required: Authorization: Bearer <secret>.",
            statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(Context);
    }
}
