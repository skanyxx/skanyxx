using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Host.Navigation;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Home is Chat with the seed (D5, D017). The owner goes to the model step instead while the model is not configured
/// (or kagent cannot say), because Chat cannot answer before it is. Otherwise a person's chosen landing page wins,
/// but only a panel their roles let them open: a stored choice never opens a page the nav would not show them.
/// </summary>
public sealed class IndexModel(IMediator mediator, IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Caller.IsOwner(User) && (await mediator.Send(new ModelSettingsQuery(), ct)).Value is not { Configured: true })
            return LocalRedirect("/Model");
        if (Caller.UserId(User) is { } userId
            && (await mediator.Send(new GetPreferencesQuery(userId), ct)).Value?.Effective.Landing is { } landing and not "auto"
            && NavCatalog.LinksFor(User, configuration, Url, "/Index").FirstOrDefault(l => l.Item.Key == landing) is { } link)
            return LocalRedirect(link.Href);
        return LocalRedirect("/Chat");
    }
}
