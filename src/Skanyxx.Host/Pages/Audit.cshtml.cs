using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The audit explorer (D152, D314), the owner's only: the page is a shell that queries <c>/api/identity/audit/*</c>,
/// which enforce the same role. It brings the people's emails so ids read as names.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class AuditModel(IMediator mediator) : PageModel
{
    public IReadOnlyDictionary<string, string> People { get; private set; } = new Dictionary<string, string>();

    public string UserId { get; private set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        People = (await mediator.Send(new ListPeopleQuery(), ct)).Value!.ToDictionary(p => p.Id, p => p.Email);
        UserId = Caller.UserId(User)!;
    }
}
