using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class RecordStudioRepoHandler(MemoryDbContext db, ILogger<RecordStudioRepoHandler> logger)
    : IRequestHandler<RecordStudioRepoCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(RecordStudioRepoCommand command, CancellationToken ct)
    {
        var repo = command.Repo;
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memory_studio_repo (id, repo_id, repo_created_at, full_name, recorded_at, recorded_by)
            VALUES ({StudioRepo.SingletonId}, {repo.Id}, {repo.CreatedAt}, {repo.FullName}, {now}, {command.Actor})
            ON CONFLICT (id) DO UPDATE
            SET repo_id = excluded.repo_id, repo_created_at = excluded.repo_created_at, full_name = excluded.full_name,
                recorded_at = excluded.recorded_at, recorded_by = excluded.recorded_by
            """, ct);
        logger.LogWarning("Studio agent repo recorded by {Actor}: {Repo} (git id {RepoId}, created {CreatedAt})", command.Actor, repo.FullName, repo.Id, repo.CreatedAt);
        return Outcome<bool>.Ok(true);
    }
}
