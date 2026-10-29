using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Investigate (D340): workflows of kagent agents, their runs and reports. The owner, supervisors and builders; never an
/// employee. Its script calls <c>api/investigate</c>, whose roles and ownership checks are the rule (D341).
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor},{SkanyxxRoles.Builder}")]
public sealed class InvestigateModel : PageModel
{
    public bool IsSupervisor => Caller.IsSupervisor(User);
}
