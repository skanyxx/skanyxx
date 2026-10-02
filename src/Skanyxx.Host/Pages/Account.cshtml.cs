using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The signed-in person's own account, and linking a Microsoft account to it (D3): the only way an existing local
/// account gets a Microsoft login. The link is started with a POST (antiforgery) that carries the person's current
/// password (D10: a session cookie alone cannot link) and is bound to their user id; the result comes back to
/// <see cref="OnGetLinkedAsync"/>, which accepts only a Microsoft sign-in this user started. The owner cannot link (D11).
/// </summary>
public sealed class AccountModel(IMediator mediator) : PageModel
{
    public EntraStatus Microsoft { get; private set; } = null!;

    /// <summary>Write-only: bound on the link POST, never rendered.</summary>
    [BindProperty]
    public string? Password { get; set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostLinkMicrosoftAsync(CancellationToken ct)
    {
        try
        {
            var challenge = await mediator.Send(new EntraChallengeQuery(Url.Page("/Account", "Linked")!, LinkUserId: Actor, LinkPassword: Password ?? ""), ct);
            if (challenge.Value is { } started)
                return Challenge(started.Properties, started.Scheme);
            if (challenge.Status == OutcomeStatus.NotFound)
                return NotFound();
            Error = challenge.Message;
            Response.StatusCode = challenge.Status.HttpStatus();
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        Password = null;
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnGetLinkedAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new LinkEntraCommand(Actor), ct);
        if (outcome.Value is not null)
            Message = "Your Microsoft account is linked. Your roles and teams now come from your Microsoft groups.";
        else
        {
            Error = outcome.Message;
            Response.StatusCode = outcome.Status.HttpStatus();
        }
        await LoadAsync(ct);
        return Page();
    }

    private string Actor => Caller.UserId(User)!;

    private async Task LoadAsync(CancellationToken ct) => Microsoft = (await mediator.Send(new EntraStatusQuery(Actor), ct)).Value!;
}
