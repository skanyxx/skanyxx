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
/// The owner's people admin (D026): invite (the link is shown on this response only), change roles, disable/enable,
/// revoke pending invites. The same commands as <c>api/identity/invites</c> and <c>api/identity/people</c>.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class PeopleModel(IMediator mediator) : PageModel
{
    public IReadOnlyList<PersonDto> People { get; private set; } = [];

    public IReadOnlyList<PendingInvite> Invites { get; private set; } = [];

    /// <summary>Set only on the response to a successful invite; never stored or shown again.</summary>
    public string? InviteLinkOnce { get; private set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostInviteAsync(string? email, string[]? roles, CancellationToken ct) =>
        await RunAsync(async () =>
        {
            var outcome = await mediator.Send(new CreateInviteCommand(Actor, email?.Trim() ?? "", roles ?? []), ct);
            if (outcome.Value is { } issued)
            {
                InviteLinkOnce = issued.Link;
                Response.Headers.CacheControl = "no-store";
                Message = $"Invite created for {email?.Trim()}. Copy the link now: it is not shown again.";
            }
            else
                Error = outcome.Message;
        }, ct);

    public async Task<IActionResult> OnPostRolesAsync(string id, string[]? roles, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new SetRolesCommand(Actor, id, roles ?? []), ct), "Roles saved."), ct);

    public async Task<IActionResult> OnPostDisableAsync(string id, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new SetDisabledCommand(Actor, id, Disabled: true), ct), "Account disabled."), ct);

    public async Task<IActionResult> OnPostEnableAsync(string id, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new SetDisabledCommand(Actor, id, Disabled: false), ct), "Account enabled."), ct);

    public async Task<IActionResult> OnPostRevokeAsync(string id, CancellationToken ct) =>
        await RunAsync(async () => Report(await mediator.Send(new RevokeInviteCommand(Actor, id), ct), "Invite revoked."), ct);

    private string Actor => Caller.UserId(User)!;

    private void Report<T>(Outcome<T> outcome, string done)
    {
        if (outcome.Status == OutcomeStatus.Ok)
            Message = done;
        else
            Error = outcome.Message;
    }

    private async Task<IActionResult> RunAsync(Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        People = (await mediator.Send(new ListPeopleQuery(), ct)).Value!;
        Invites = (await mediator.Send(new ListInvitesQuery(), ct)).Value!;
    }
}
