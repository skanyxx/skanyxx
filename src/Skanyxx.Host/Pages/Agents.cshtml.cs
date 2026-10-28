using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Agents (requirements.md §3.3, D250, D312), laid out like kagent-ui: a card grid with search and filters, an agent's
/// details, and the create/edit form in sections. The page holds no rule and never talks to kagent: its script calls
/// <c>api/agents</c>, whose handlers decide (D254). Supervisors and the owner save live agents; everyone else submits the
/// same form as a Studio proposal. Hiding what a role cannot use is cosmetic.
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor},{SkanyxxRoles.Builder},{SkanyxxRoles.Employee}")]
public sealed class AgentsModel : PageModel
{
    public bool CanWrite => Caller.IsSupervisor(User);

    /// <summary>Builders read every agent with its prompt, so they may propose a change to one; employees propose new agents.</summary>
    public bool CanProposeChanges => !CanWrite && User.IsInRole(SkanyxxRoles.Builder);
}
