using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Settings;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The owner's installation settings (D350): the same commands as <c>api/identity/settings</c>. Each field is named by
/// its catalog key; only the posted section's fields are saved, so one section's save never touches another's.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class SettingsModel(IMediator mediator, IConfiguration configuration, IReadOnlyList<IModule> modules) : PageModel
{
    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    public bool CloudToolsLicensed => CloudToolsFeature.IsLicensed(configuration);

    public bool EntraOn => EntraSwitch.IsOn(configuration);

    public SandboxStatus Sandboxes => new(
        modules.Any(m => m.ModuleId == "sandboxes"),
        configuration.GetValue<bool>("Sandboxes:Enabled"),
        !string.IsNullOrWhiteSpace(configuration["Sandboxes:Address"]),
        configuration.GetSection("Sandboxes:AllowedImages").GetChildren().Count(),
        configuration.GetValue<bool>("Sandboxes:NetworkIsolationConfirmed"));

    [TempData]
    public string? Message { get; set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(string? section, CancellationToken ct)
    {
        var fields = InstallationSettings.All.Where(s => s.Section == section).ToList();
        if (fields.Count == 0)
            return BadRequest();

        var values = fields.ToDictionary(s => s.Key, s => s.Kind == SettingKind.Bool
            ? (Request.Form[s.Key].Contains("true") ? "true" : "false")
            : Request.Form[s.Key].ToString().Trim());
        // A locked paid feature has a disabled checkbox, which posts nothing: that is not a request to change it.
        if (!CloudToolsLicensed)
            values.Remove(InstallationSettings.CloudTools);

        try
        {
            var outcome = await mediator.Send(new SaveInstallationSettingsCommand(Caller.UserId(User)!, values), ct);
            if (outcome.Status == OutcomeStatus.Ok)
            {
                Message = outcome.Value!.Count == 0 ? "Nothing changed." : $"{section} saved. Changes are in effect.";
                return RedirectToPage();
            }
            Error = outcome.Message;
            Response.StatusCode = outcome.Status.HttpStatus();
        }
        catch (ValidationException ex)
        {
            Error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct());
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        await LoadAsync(ct);
        return Page();
    }

    public string Value(string key) => Values.GetValueOrDefault(key, "");

    private async Task LoadAsync(CancellationToken ct) => Values = (await mediator.Send(new GetInstallationSettingsQuery(), ct)).Value!;
}
