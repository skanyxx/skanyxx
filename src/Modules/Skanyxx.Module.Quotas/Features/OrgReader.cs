using MediatR;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>The org tree through identity's queries (never its tables).</summary>
internal sealed class OrgReader(IMediator mediator)
{
    public async Task<OrgSnapshot> ReadAsync(CancellationToken ct) => new(
        (await mediator.Send(new ListPeopleQuery(), ct)).Value!,
        (await mediator.Send(new ListTeamsQuery(), ct)).Value!,
        (await mediator.Send(new ListDepartmentsQuery(), ct)).Value!);
}
