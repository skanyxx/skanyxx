using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Usage analytics (requirements.md §3.11): everyone their own; the owner the organization, departments, teams and people;
/// a supervisor their teams. Scope checks and the CSV export (owner, audited) are <c>api/analytics/*</c>'s.
/// </summary>
public sealed class AnalyticsModel : PageModel
{
    public bool IsOwner => Caller.IsOwner(User);

    public void OnGet()
    {
    }
}
