using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Host.Setup;

/// <summary>
/// The one setup block every panel shows: <c>@await Component.InvokeAsync("SetupGuide", new { id = "studio" })</c>.
/// Owner and supervisors get the guide (open while something is missing); everyone else only "ask your admin" when
/// the feature is not set up.
/// </summary>
public sealed class SetupGuideViewComponent(SetupInspector inspector) : ViewComponent
{
    public IViewComponentResult Invoke(string id)
    {
        var guide = SetupGuides.Find(id) ?? throw new ArgumentException($"No setup guide '{id}'.", nameof(id));
        return View(new SetupPanel(guide, inspector.Inspect(guide), SetupAccess.CanRead(HttpContext.User)));
    }
}
