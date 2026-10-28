using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skanyxx.Host.Pages;

/// <summary>Where a signed-in person lands when a page needs a role they do not have: always a 403.</summary>
public sealed class AccessDeniedModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
