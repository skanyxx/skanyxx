using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.SandboxTokens;

/// <summary>
/// D076: signs a sandbox's token and makes it the agent id's one live token, in one statement (another replica issuing
/// for the same activation cannot leave two). An agent id already bound to a different sandbox or owner is refused: the
/// id is the grants key, and re-binding it would hand one sandbox's grants to another. Expired rows are swept on the way.
/// </summary>
internal sealed class IssueSandboxMemoryTokenHandler(
    MemoryDbContext db, IOptions<MemoryOptions> options, ILogger<IssueSandboxMemoryTokenHandler> logger)
    : IRequestHandler<IssueSandboxMemoryTokenCommand, Outcome<SandboxMemoryToken>>
{
    public async Task<Outcome<SandboxMemoryToken>> Handle(IssueSandboxMemoryTokenCommand command, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.SandboxTokensOn)
            return Outcome<SandboxMemoryToken>.Unavailable(
                "Sandbox memory is off: Memory:SandboxMcpPort and Memory:SandboxTokenKey are not both set.");

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(settings.SandboxTokenHours);
        var token = SandboxTokenCodec.Sign(new(command.SandboxName, command.AgentId, command.OwnerId, expires), settings.SandboxTokenKey);
        var hash = SandboxTokenCodec.Hash(token);

        await db.SandboxTokens.Where(t => t.ExpiresAt < now.UtcDateTime).ExecuteDeleteAsync(ct);
        var saved = await db.Database.SqlQuery<string>($"""
            INSERT INTO memory_sandbox_tokens (agent_id, sandbox_name, owner_id, token_hash, expires_at, created_at)
            VALUES ({command.AgentId}, {command.SandboxName}, {command.OwnerId}, {hash}, {expires.UtcDateTime}, {now.UtcDateTime})
            ON CONFLICT (agent_id) DO UPDATE SET token_hash = EXCLUDED.token_hash, expires_at = EXCLUDED.expires_at, created_at = EXCLUDED.created_at
            WHERE memory_sandbox_tokens.sandbox_name = EXCLUDED.sandbox_name AND memory_sandbox_tokens.owner_id = EXCLUDED.owner_id
            RETURNING agent_id AS "Value"
            """).ToListAsync(ct);
        if (saved.Count == 0)
            return Outcome<SandboxMemoryToken>.Conflict(null,
                $"Memory agent id '{command.AgentId}' is bound to another sandbox or owner; it is not re-bound.");

        logger.LogInformation("Sandbox memory token issued for {Sandbox} as {AgentId} (owner {OwnerId}, expires {ExpiresAt:O})",
            command.SandboxName, command.AgentId, command.OwnerId, expires);
        return Outcome<SandboxMemoryToken>.Ok(new SandboxMemoryToken(command.AgentId, token, expires));
    }
}
