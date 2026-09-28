using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>Browser sign-in (session cookie). Before the owner exists, sends the visitor to setup instead.</summary>
[AllowAnonymous]
public sealed class LoginModel(IMediator mediator) : PageModel
{
    // Nullable: MVC binds an empty or missing field as null.
    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    /// <summary>Optional break-glass: the owner with <c>Identity:BootstrapToken</c> signs in past a lockout (D081).</summary>
    [BindProperty]
    public string? BootstrapToken { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!(await mediator.Send(new IdentityStatusQuery(), ct)).Value!.Bootstrapped)
            return RedirectToPage("/Setup");
        return User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl) : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new SignInCommand(Email?.Trim() ?? "", Password ?? "", UseCookie: true,
                string.IsNullOrEmpty(BootstrapToken) ? null : BootstrapToken), ct);
            if (outcome.Value is not null)
                return LocalRedirect(SafeReturnUrl);
            Error = outcome.Message;
        }
        catch (ValidationException)
        {
            Error = "Enter your email and password.";
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        return Page();
    }

    // Only same-site paths: an absolute ReturnUrl would make this page an open redirect.
    private string SafeReturnUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
}
