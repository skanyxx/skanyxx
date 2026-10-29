using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>
/// The agent repo's CI (D362): <c>Authorization: Bearer &lt;Studio:Ci:Token&gt;</c>, not a person's session. <c>404</c>
/// while no token is configured, <c>401</c> for any other token (compared in constant time, never logged), <c>200</c>
/// with <c>valid</c> and the problems otherwise. Rate limited and origin checked with the rest of <c>/api/studio</c>.
/// </summary>
internal sealed class CiValidateEndpoint(IMediator mediator, IOptions<StudioOptions> options, ILogger<CiValidateEndpoint> logger) : Endpoint<CiValidateRequest>
{
    public override void Configure()
    {
        Post("api/studio/ci/validate");
        AllowAnonymous();
        // Both files at their cap, JSON-escaped.
        Options(b => b.WithMetadata(new RequestSizeLimitAttribute(400 * 1024)));
    }

    public override async Task HandleAsync(CiValidateRequest req, CancellationToken ct)
    {
        var expected = options.Value.Ci.Token;
        if (string.IsNullOrWhiteSpace(expected))
        {
            await Send.ResultAsync(Results.Problem("CI validation is off: the owner sets Studio:Ci:Token.", statusCode: StatusCodes.Status404NotFound));
            return;
        }
        if (!Matches(HttpContext.Request.Headers.Authorization.ToString(), expected))
        {
            logger.LogWarning("Studio CI validation refused: missing or wrong CI token from {RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            await Send.ResultAsync(Results.Problem("A valid CI token is required (Authorization: Bearer).", statusCode: StatusCodes.Status401Unauthorized));
            return;
        }
        await Send.ResultAsync((await mediator.Send(new ValidateAgentFilesQuery(req.Name ?? "", req.AgentYaml ?? "", req.GrantsYaml ?? ""), ct)).ToHttp());
    }

    private static bool Matches(string header, string expected)
    {
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(header[scheme.Length..].Trim()));
        return CryptographicOperations.FixedTimeEquals(presented, SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
