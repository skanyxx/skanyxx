using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Host.Setup;

/// <summary>
/// The one setup block every panel shows: <c>@await Component.InvokeAsync("SetupGuide", new { id = "studio" })</c>.
/// Owner and supervisors get the guide while something is missing; everyone else only "ask your admin". A feature that
/// is set up shows nothing on its panel; <c>whenReady</c> (the Setup guides page) shows its guide anyway.
/// </summary>
public sealed class SetupGuideViewComponent(SetupInspector inspector) : ViewComponent
{
    public IViewComponentResult Invoke(string id, bool whenReady = false)
    {
        var guide = SetupGuides.Find(id) ?? throw new ArgumentException($"No setup guide '{id}'.", nameof(id));
        var state = inspector.Inspect(guide);
        if (state.Ready && !whenReady)
            return Content(string.Empty);
        return View(new SetupPanel(guide, state, SetupAccess.CanRead(HttpContext.User)));
    }
}
