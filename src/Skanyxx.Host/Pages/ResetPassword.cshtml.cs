using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>
/// A password-reset link (D156): shows the account's email, takes the new password, ends every session and sends the
/// person to sign in. Anonymous, counted in the one-time-link rate-limit window on GET and POST, like the invite page.
/// </summary>
[AllowAnonymous]
public sealed class ResetPasswordModel(IMediator mediator) : PageModel
{
    private const string InvalidLink = "This password-reset link is invalid or has expired. Ask for a new one.";

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    public PasswordResetDetails? Reset { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Every response: the token is in the URL, so keep it out of caches and Referer headers.</summary>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LookUpAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new CompletePasswordResetCommand(Token ?? "", Password ?? ""), ct);
            if (outcome.Status == OutcomeStatus.Ok)
                return LocalRedirect("/Login?reset=true");
            Error = outcome.Message;
            Response.StatusCode = outcome.Status.HttpStatus();
            return Page();
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
        }
        // A 400 keeps the form up while the link is still good; one that is not is the 404 the lookup reports.
        await LookUpAsync(ct);
        if (Reset is not null)
            Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    private async Task LookUpAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new PasswordResetStatusQuery(Token ?? ""), ct);
            Reset = outcome.Value;
            if (Reset is null)
            {
                Error ??= outcome.Message;
                Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
        catch (ValidationException)
        {
            Error ??= InvalidLink;
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
