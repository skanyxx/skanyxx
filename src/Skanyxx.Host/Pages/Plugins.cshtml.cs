using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Plugins (P1–P12): what is installed and how its servers stand, and "Add plugin" from the built-in catalog, a git
/// commit or a zip. The page holds no rule and reads nothing itself: its script calls <c>api/plugins</c> with the
/// person's cookie, so the module's checks are the only ones. Installing opens a studio pull request; a supervisor
/// reviews and merges it on the Studio page.
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor},{SkanyxxRoles.Builder}")]
public sealed class PluginsModel : PageModel
{
    public StudioUser Me => StudioUser.From(User);
}
