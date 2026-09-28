using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skanyxx.Host.Pages
{
    [AllowAnonymous]
    public class OfflineModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
