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
/// The owner's Microsoft Entra ID sign-in settings (D027): tenant, client, client secret (write-only: the page shows
/// only whether one is set and never renders it back) and the group → roles/teams map. The same command as
/// <c>PUT api/identity/entra/settings</c>; a save applies to the next sign-in without a restart.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class EntraModel(IMediator mediator) : PageModel
{
    /// <summary>Empty rows offered for new groups under the saved ones.</summary>
    public const int BlankRows = 3;

    public EntraSettingsDto Settings { get; private set; } = null!;

    public IReadOnlyList<TeamDto> Teams { get; private set; } = [];

    [BindProperty]
    public bool Enabled { get; set; }

    [BindProperty]
    public string? TenantId { get; set; }

    [BindProperty]
    public string? ClientId { get; set; }

    /// <summary>Write-only: bound on POST, never rendered.</summary>
    [BindProperty]
    public string? ClientSecret { get; set; }

    [BindProperty]
    public List<EntraGroupRow> Groups { get; set; } = [];

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new SaveEntraSettingsCommand(Caller.UserId(User)!, Enabled, TenantId?.Trim() ?? "", ClientId?.Trim() ?? "",
                ClientSecret, [.. Groups.Where(g => !string.IsNullOrWhiteSpace(g.GroupId))
                    .Select(g => new EntraGroupMapDto(g.GroupId!.Trim(), g.Label, g.Roles, g.Teams))]), ct);
            if (outcome.Status == OutcomeStatus.Ok)
                Message = outcome.Message ?? "Saved. The next Microsoft sign-in uses these settings.";
            else
            {
                Error = outcome.Message;
                Response.StatusCode = outcome.Status.HttpStatus();
            }
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        await LoadAsync(ct, keepForm: Error is not null);
        return Page();
    }

    /// <summary>A refused save keeps what the owner typed (except the secret); otherwise the form shows what is stored.</summary>
    private async Task LoadAsync(CancellationToken ct, bool keepForm = false)
    {
        Settings = (await mediator.Send(new GetEntraSettingsQuery(), ct)).Value!;
        Teams = (await mediator.Send(new ListTeamsQuery(), ct)).Value!;
        if (!keepForm)
        {
            Enabled = Settings.Enabled;
            TenantId = Settings.TenantId;
            ClientId = Settings.ClientId;
            Groups = [.. Settings.Groups.Select(g => new EntraGroupRow { GroupId = g.GroupId, Label = g.Label, Roles = [.. g.Roles], Teams = [.. g.Teams] })];
        }
        ClientSecret = null;
        Groups = [.. Groups.Where(g => !string.IsNullOrWhiteSpace(g.GroupId)), .. Enumerable.Range(0, BlankRows).Select(_ => new EntraGroupRow())];
    }
}
