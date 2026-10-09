using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages
{
    /// <summary>Owner only, like every <c>api/settings</c> call it makes (the theme read is the one open to everyone).</summary>
    [Authorize(Roles = SkanyxxRoles.Owner)]
    public class SettingsModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
