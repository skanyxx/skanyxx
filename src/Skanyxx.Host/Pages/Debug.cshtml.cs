using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages
{
    /// <summary>Owner only, like its nav link and every <c>api/debug</c> call it makes (D247).</summary>
    [Authorize(Roles = SkanyxxRoles.Owner)]
    public class DebugModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
