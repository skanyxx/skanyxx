using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Token quotas: the owner sets person, team and department limits and model prices and decides every request; a
/// supervisor sees their teams and sets their members' limits within the team quota. Every check is
/// <c>api/quotas/*</c>'s; this page only draws what it answers.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner + "," + SkanyxxRoles.Supervisor)]
public sealed class QuotasModel : PageModel
{
    public bool IsOwner => Caller.IsOwner(User);

    public void OnGet()
    {
    }
}
