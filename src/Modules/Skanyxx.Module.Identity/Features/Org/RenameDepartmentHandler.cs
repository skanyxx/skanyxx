using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>The name only; the slug is the department's id for good.</summary>
internal sealed class RenameDepartmentHandler(AccountsDbContext db, ClientAddress client, ILogger<RenameDepartmentHandler> logger)
    : IRequestHandler<RenameDepartmentCommand, Outcome<DepartmentDto>>
{
    public async Task<Outcome<DepartmentDto>> Handle(RenameDepartmentCommand command, CancellationToken ct)
    {
        var renamed = await db.Departments.Where(d => d.Slug == command.Slug)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, command.Name), ct);
        if (renamed == 0)
            return Outcome<DepartmentDto>.NotFound($"No department '{command.Slug}'.");

        logger.LogWarning("Department {Slug} renamed by {ActorUserId} from {RemoteIp}", command.Slug, command.ActorId, client.Current);
        return Outcome<DepartmentDto>.Ok(new DepartmentDto(command.Slug, command.Name));
    }
}
