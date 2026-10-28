using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Models (D323): kagent's ModelConfigs as a list with create, edit and delete, like kagent-ui's Models page; the first
/// hour's model step is creating the default one (D3). The page holds no rule and never talks to kagent: its script calls
/// <c>api/models</c>, whose endpoints decide (the owner writes, a supervisor reads). An API key is typed here and sent
/// once; it is never rendered back.
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor}")]
public sealed class ModelModel : PageModel
{
    public bool CanWrite => Caller.IsOwner(User);
}
