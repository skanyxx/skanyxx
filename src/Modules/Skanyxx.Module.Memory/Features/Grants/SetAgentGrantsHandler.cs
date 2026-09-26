using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Grants;

internal sealed class SetAgentGrantsHandler(MemoryDbContext db, AccessPolicy access)
    : IRequestHandler<SetAgentGrantsCommand, Outcome<IReadOnlyList<GrantEntry>>>
{
    /// <summary>First key of the two-key advisory lock, so this feature's locks never collide with other lock users.</summary>
    private const int GrantsLockSpace = 0x4D454D01;

    public async Task<Outcome<IReadOnlyList<GrantEntry>>> Handle(SetAgentGrantsCommand command, CancellationToken ct)
    {
        if (!access.IsSupervisor(command.Caller))
            return Outcome<IReadOnlyList<GrantEntry>>.Forbidden("Only a supervisor may change agent grants.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Delete-then-insert is not atomic against a second writer for the same agent under READ COMMITTED
        // (grants would merge or hit the primary key); serialize writers per agent for this transaction.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({GrantsLockSpace}, hashtext({command.AgentId}))", ct);
        await db.Grants.Where(g => g.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
        db.Grants.AddRange(command.Grants.Select(g => new AgentGrant
        {
            AgentId = command.AgentId,
            Scope = g.Scope,
            CanSearch = g.CanSearch,
            CanUpsert = g.CanUpsert
        }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Outcome<IReadOnlyList<GrantEntry>>.Ok(command.Grants);
    }
}
