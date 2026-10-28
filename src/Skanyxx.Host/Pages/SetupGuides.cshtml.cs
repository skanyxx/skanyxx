using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Host.Setup;

namespace Skanyxx.Host.Pages;

/// <summary>Every module's setup guide on one page, with what this install is missing; the owner and supervisors only.</summary>
[Authorize(Roles = SetupAccess.Roles)]
public sealed class SetupGuidesModel : PageModel
{
    public IReadOnlyList<SetupGuide> Guides => SetupGuides.All;
}
