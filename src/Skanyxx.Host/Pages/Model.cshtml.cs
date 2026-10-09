using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The owner's model step of the first hour (roles.md, D3): which model the seed agent runs on, and its API key. Saved
/// into kagent's ModelConfig through kagent's API (the same command as <c>PUT api/model</c>); the key is never shown,
/// stored or logged here. On success the owner goes on to Chat with the seed (D5).
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class ModelModel(IMediator mediator) : PageModel
{
    // Nullable: MVC binds an empty or missing field as null.
    [BindProperty]
    public string? Provider { get; set; }

    [BindProperty]
    public string? ModelId { get; set; }

    [BindProperty]
    public string? BaseUrl { get; set; }

    [BindProperty]
    public string? ApiKey { get; set; }

    public ModelSettings? Current { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        Provider = Current?.Provider ?? ModelProviders.OpenAI;
        ModelId = Current?.Model;
        BaseUrl = Current?.BaseUrl;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new SaveModelSettingsCommand(
                Caller.UserId(User)!, Provider?.Trim() ?? "", ModelId?.Trim() ?? "", ApiKey?.Trim(), BaseUrl?.Trim()), ct);
            if (outcome.Status == OutcomeStatus.Ok)
                return LocalRedirect("/Chat");
            Error = outcome.Message;
            Response.StatusCode = outcome.Status.HttpStatus();
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        ApiKey = null;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new ModelSettingsQuery(), ct);
        Current = outcome.Value;
        if (outcome.Value is null)
            Error ??= outcome.Message;
    }
}
