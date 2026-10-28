using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages
{
    /// <summary>Owner only, like every <c>api/cloudtools</c> call it makes (D116, D248).</summary>
    [Authorize(Roles = SkanyxxRoles.Owner)]
    public class CloudToolsModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
