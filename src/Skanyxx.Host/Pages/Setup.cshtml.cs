using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>Owner bootstrap (D025): the same command and guard as <c>POST api/identity/bootstrap</c>, then a cookie sign-in and the model step.</summary>
[AllowAnonymous]
public sealed class SetupModel(IMediator mediator) : PageModel
{
    // Nullable: MVC binds an empty or missing field as null.
    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? DisplayName { get; set; }

    [BindProperty]
    public string? BootstrapToken { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) =>
        (await mediator.Send(new IdentityStatusQuery(), ct)).Value!.Bootstrapped ? RedirectToPage("/Login") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var email = Email?.Trim() ?? "";
            var password = Password ?? "";
            var outcome = await mediator.Send(new BootstrapOwnerCommand(
                email, password, DisplayName, string.IsNullOrEmpty(BootstrapToken) ? null : BootstrapToken, DirectLoopback.Is(HttpContext)), ct);
            switch (outcome.Status)
            {
                case OutcomeStatus.Created:
                    await mediator.Send(new SignInCommand(email, password, UseCookie: true), ct);
                    // The first hour goes on with the owner's model step, then Chat with the seed (first-hour.md, D5).
                    return LocalRedirect("/Model");
                case OutcomeStatus.Conflict:
                    return RedirectToPage("/Login");
                default:
                    Error = outcome.Message;
                    break;
            }
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        return Page();
    }
}
