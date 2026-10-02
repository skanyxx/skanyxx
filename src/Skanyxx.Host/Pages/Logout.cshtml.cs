using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>
/// POST only (antiforgery-checked), so a link or image on another page cannot sign the user out. The same sign-out as
/// the API: every session of the user ends, not just this browser's cookie.
/// </summary>
public sealed class LogoutModel(IMediator mediator) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await mediator.Send(new SignOutCommand(Caller.UserId(User)!), ct);
        return RedirectToPage("/Login");
    }
}
