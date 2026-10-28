using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Where a signed-in person lands on a page their role lacks. Not <c>/Login</c>: that sends a signed-in visitor back
/// to the return URL, which looped between the two.
/// </summary>
[AllowAnonymous]
public sealed class AccessDeniedModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
