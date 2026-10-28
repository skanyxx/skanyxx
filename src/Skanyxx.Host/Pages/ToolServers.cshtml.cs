using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// kagent's MCP tool servers, like kagent's own Tool servers page (D312): the owner adds, edits and deletes; a supervisor
/// reads and refreshes; nobody else has the page. Its script calls <c>api/toolservers</c>, whose roles are the rule.
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor}")]
public sealed class ToolServersModel : PageModel
{
    public bool IsOwner => Caller.IsOwner(User);
}
