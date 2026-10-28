namespace Skanyxx.Host.Tests;

/// <summary>
/// D248 (Chrome run): site.js read the theme on every page, and the theme is a signed-in read, so each public page
/// (Privacy, Offline, Access denied) logged a 401 in the console. A page without a signed-in header keeps the default.
/// </summary>
public sealed class SiteScriptTests
{
    [Fact]
    public void TheThemeIsReadOnlyWhenSomeoneIsSignedIn()
    {
        var script = File.ReadAllText(Repo.Path("src", "Skanyxx.Host", "wwwroot", "js", "site.js"));

        var guard = script.IndexOf("if (!document.querySelector('.header-user'))", StringComparison.Ordinal);
        var fetch = script.IndexOf("fetch('/api/settings/theme')", StringComparison.Ordinal);

        Assert.True(guard >= 0 && fetch > guard, "the theme fetch must follow the signed-in check");
    }
}
