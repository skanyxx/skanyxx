using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The admin Dashboard (requirements.md §3.1): health, usage at a glance, compact charts, recent admin events. The owner;
/// a supervisor read-only (no per-person lists, no audit). Data from <c>api/dashboard/*</c>, which checks the same roles.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner + "," + SkanyxxRoles.Supervisor)]
public sealed class DashboardModel : PageModel
{
    public bool IsOwner => Caller.IsOwner(User);

    public void OnGet()
    {
    }
}
