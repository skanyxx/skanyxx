using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Sandboxes on Google AX (Experimental, D063/D064): supervisors list, run, watch and stop sandbox tasks. The page holds
/// no rule and never talks to AX: its script calls <c>api/sandboxes</c> with the person's cookie (as Chat calls
/// <c>api/chat</c>), so the module's checks (allowed images, caps, creator-or-supervisor) are the only ones (D070, D072).
/// </summary>
[Authorize(Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor}")]
public sealed class SandboxesModel(IConfiguration configuration) : PageModel
{
    /// <summary>The module registers nothing while off, and every <c>api/sandboxes</c> route is a 404.</summary>
    public bool Enabled { get; } = configuration.GetValue<bool>("Sandboxes:Enabled");

    /// <summary>Offered as suggestions only; the module decides what may run.</summary>
    public IReadOnlyList<string> AllowedImages { get; } = configuration.GetSection("Sandboxes:AllowedImages").Get<string[]>() ?? [];

    public string? Me => Caller.UserId(User);
}
