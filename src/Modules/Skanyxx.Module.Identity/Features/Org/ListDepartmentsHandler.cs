using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

internal sealed class ListDepartmentsHandler(AccountsDbContext db) : IRequestHandler<ListDepartmentsQuery, Outcome<IReadOnlyList<DepartmentDto>>>
{
    public async Task<Outcome<IReadOnlyList<DepartmentDto>>> Handle(ListDepartmentsQuery query, CancellationToken ct) =>
        Outcome<IReadOnlyList<DepartmentDto>>.Ok(
            await db.Departments.AsNoTracking().OrderBy(d => d.Slug).Select(d => new DepartmentDto(d.Slug, d.Name)).ToListAsync(ct));
}
