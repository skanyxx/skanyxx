using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Interfaces;

namespace Skanyxx.Module.Settings.Controllers;

/// <summary>
/// The one setting every signed-in person reads (site.js on each page). Its literal route wins over
/// <c>GET api/settings/{key}</c>, which stays owner only; writing the theme is the owner's.
/// </summary>
[ApiController]
public class ThemeController(ISettingsService settings) : ControllerBase
{
    public const string Key = "theme";

    [HttpGet("api/settings/" + Key)]
    public async Task<ActionResult<string>> Get()
    {
        var value = await settings.GetAsync(Key);
        if (value == null) return NotFound();
        return Ok(value);
    }
}
