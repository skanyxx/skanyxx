using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Agents (requirements.md §3.3, D250): list, details, create, edit and delete, like kagent-ui. The page holds no rule
/// and never talks to kagent: its script calls <c>api/agents</c>, whose handlers decide (D254). Hiding the buttons a
/// role cannot use is cosmetic.
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor},{SkanyxxRoles.Builder},{SkanyxxRoles.Employee}")]
public sealed class AgentsModel : PageModel
{
    public bool CanWrite => Caller.IsSupervisor(User);

    public bool IsBuilder => User.IsInRole(SkanyxxRoles.Builder);
}
