using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>
/// "Forgot your password?" (D156): the same answer for every email, whether or not an account has it; the link is
/// emailed in the background. <c>404</c> when SMTP is not configured. The POST counts in the sign-in rate-limit window.
/// </summary>
[AllowAnonymous]
public sealed class ForgotPasswordModel(IMediator mediator) : PageModel
{
    [BindProperty]
    public string? Email { get; set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) =>
        (await mediator.Send(new IdentityStatusQuery(), ct)).Value!.PasswordResetByEmail ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new RequestPasswordResetCommand(Email?.Trim() ?? ""), ct);
            if (outcome.Status == OutcomeStatus.NotFound)
                return NotFound();
            Message = "If that email belongs to an account that can reset its password, a link is on its way. It works once, for a short time.";
            Email = null;
        }
        catch (ValidationException)
        {
            Error = "Enter your email.";
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        return Page();
    }
}
