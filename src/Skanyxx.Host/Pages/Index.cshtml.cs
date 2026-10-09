using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Home is Chat with the seed (D5, D017), not the leftover Dashboard. The owner goes to the model step instead while
/// the model is not configured (or kagent cannot say), because Chat cannot answer before it is.
/// </summary>
public sealed class IndexModel(IMediator mediator) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Caller.IsOwner(User) && (await mediator.Send(new ModelSettingsQuery(), ct)).Value is not { Configured: true })
            return LocalRedirect("/Model");
        return LocalRedirect("/Chat");
    }
}
