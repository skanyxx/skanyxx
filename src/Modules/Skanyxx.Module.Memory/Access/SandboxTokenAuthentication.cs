using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Access;

/// <summary>
/// D076: the only scheme the sandbox listener (D077) accepts, and it accepts nothing anywhere else. The token must carry
/// a valid signature under <c>Memory:SandboxTokenKey</c>, be unexpired, and still be the live token of its agent id
/// (the row a stop or a re-issue deletes or replaces). The principal is that agent id and its owner, from the token;
/// no header names anyone here.
/// </summary>
internal sealed class SandboxTokenAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    MemoryDbContext db, IOptions<MemoryOptions> memory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = AgentPrincipal.SandboxSchemeName;
    public const string OwnerClaim = "skanyxx:memory:sandbox_owner";
    public const string SandboxClaim = "skanyxx:memory:sandbox";
    private const string Bearer = "Bearer ";

    private bool OnSandboxListener => memory.Value.SandboxMcpPort is int port && Context.Connection.LocalPort == port;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!OnSandboxListener || !memory.Value.SandboxTokensOn)
            return AuthenticateResult.NoResult();

        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        // Failure messages name the reason, never the value: they reach the authentication log.
        var token = header[Bearer.Length..].Trim();
        var now = DateTimeOffset.UtcNow;
        var claims = SandboxTokenCodec.Read(token, memory.Value.SandboxTokenKey, now, out var failure);
        if (claims is null)
            return AuthenticateResult.Fail(failure);

        var hash = SandboxTokenCodec.Hash(token);
        var live = await db.SandboxTokens.AsNoTracking()
            .AnyAsync(t => t.AgentId == claims.AgentId && t.TokenHash == hash && t.ExpiresAt > now.UtcDateTime, Context.RequestAborted);
        if (!live)
            return AuthenticateResult.Fail("Sandbox token revoked or replaced.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, claims.AgentId),
            new Claim(OwnerClaim, claims.OwnerId),
            new Claim(SandboxClaim, claims.SandboxName)
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    // Both memory schemes guard /mcp/memory; only the one that owns this port answers, so the response is written once.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!OnSandboxListener)
            return Task.CompletedTask;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem("A live sandbox token is required: Authorization: Bearer $SKANYXX_MEMORY_TOKEN. "
            + "An expired or revoked one is renewed by re-running the sandbox, or suspending and resuming it.",
            statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        OnSandboxListener ? base.HandleForbiddenAsync(properties) : Task.CompletedTask;
}
