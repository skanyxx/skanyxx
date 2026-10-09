using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class StudioRepoQueryHandler(MemoryDbContext db) : IRequestHandler<StudioRepoQuery, Outcome<StudioRepoIdentity?>>
{
    public async Task<Outcome<StudioRepoIdentity?>> Handle(StudioRepoQuery query, CancellationToken ct) =>
        Outcome<StudioRepoIdentity?>.Ok(await db.StudioRepos.AsNoTracking()
            .Select(r => new StudioRepoIdentity(r.RepoId, r.RepoCreatedAt, r.FullName)).FirstOrDefaultAsync(ct));
}
