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
/// Accepting an invite (D026): shows the email and roles it offers, takes a password (and display name), creates the
/// account and signs it in with a cookie. Anonymous, and counted in the invite rate-limit window on GET and POST.
/// </summary>
[AllowAnonymous]
public sealed class InviteModel(IMediator mediator) : PageModel
{
    private const string InvalidLink = "This invite link is invalid or has expired. Ask the owner for a new one.";

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? DisplayName { get; set; }

    public InviteDetails? Invite { get; private set; }

    public string? Error { get; private set; }

    /// <summary>
    /// Every response of this page, whatever the handler or branch: the token is in its URL, so keep it out of caches and
    /// out of Referer headers.
    /// </summary>
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
            var outcome = await mediator.Send(new AcceptInviteCommand(Token ?? "", Password ?? "", DisplayName, UseCookie: true), ct);
            if (outcome.Status == OutcomeStatus.Created)
                return LocalRedirect("/");
            Error = outcome.Message;
            Response.StatusCode = outcome.Status switch
            {
                OutcomeStatus.NotFound => StatusCodes.Status404NotFound,
                OutcomeStatus.Conflict => StatusCodes.Status409Conflict,
                _ => throw new InvalidOperationException($"Unexpected invite accept outcome {outcome.Status}.")
            };
            return Page();
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
        }
        // A 400 keeps the form up when the invite is still good; one that is not is the 404 the lookup reports.
        await LookUpAsync(ct);
        if (Invite is not null)
            Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    private async Task LookUpAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new InviteStatusQuery(Token ?? ""), ct);
            Invite = outcome.Value;
            if (Invite is null)
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
