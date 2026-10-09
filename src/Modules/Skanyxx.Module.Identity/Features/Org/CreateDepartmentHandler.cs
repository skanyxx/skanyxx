using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>
/// A taken slug is a 409: slugs are unique per kind and, with nothing ever deleted, never reused. <c>ON CONFLICT
/// ("Slug") DO NOTHING</c> rather than catching the unique violation, which EF would log at Error for an expected answer;
/// the target is named so a future unique index (on the name, say) fails loudly instead of posing as a taken slug.
/// </summary>
internal sealed class CreateDepartmentHandler(
    AccountsDbContext db, IdentityAudit audit, ClientAddress client, TimeProvider time, ILogger<CreateDepartmentHandler> logger)
    : IRequestHandler<CreateDepartmentCommand, Outcome<DepartmentDto>>
{
    public async Task<Outcome<DepartmentDto>> Handle(CreateDepartmentCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var created = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_org_departments ("Slug", "Name", "CreatedBy", "CreatedUtc")
            VALUES ({command.Slug}, {command.Name}, {command.ActorId}, {time.GetUtcNow()}) ON CONFLICT ("Slug") DO NOTHING
            """, ct);
        if (created == 0)
            return Outcome<DepartmentDto>.Conflict(null, $"A department with the slug '{command.Slug}' already exists.");
        await audit.WriteAsync(AuditActions.DepartmentCreated, command.ActorId, command.Slug, new { name = command.Name }, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Department {Slug} created by {ActorUserId} from {RemoteIp}", command.Slug, command.ActorId, client.Current);
        return Outcome<DepartmentDto>.Created(new DepartmentDto(command.Slug, command.Name));
    }
}
