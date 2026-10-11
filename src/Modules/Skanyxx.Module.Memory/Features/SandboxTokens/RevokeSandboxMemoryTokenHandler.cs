using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.SandboxTokens;

/// <summary>
/// D076: deletes the live token row, so the sandbox's token is refused from the next request on. A database that does
/// not answer is <see cref="OutcomeStatus.Unavailable"/>: the caller (a stop) refuses, and the token is still live.
/// </summary>
internal sealed class RevokeSandboxMemoryTokenHandler(MemoryDbContext db, ILogger<RevokeSandboxMemoryTokenHandler> logger)
    : IRequestHandler<RevokeSandboxMemoryTokenCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(RevokeSandboxMemoryTokenCommand command, CancellationToken ct)
    {
        int deleted;
        try
        {
            deleted = await db.SandboxTokens.Where(t => t.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
        }
        // EF wraps a refused connection in InvalidOperationException ("likely due to a transient failure").
        catch (Exception ex) when (!ct.IsCancellationRequested && (ex is DbException or TimeoutException || ex.InnerException is DbException))
        {
            logger.LogWarning("Sandbox memory token of {AgentId} could not be revoked: {Error}", command.AgentId, ex.Message);
            return Outcome<bool>.Unavailable("The memory database did not answer; the sandbox memory token was not revoked.");
        }
        if (deleted > 0)
            logger.LogInformation("Sandbox memory token revoked for {AgentId}", command.AgentId);
        return Outcome<bool>.Ok(deleted > 0);
    }
}
