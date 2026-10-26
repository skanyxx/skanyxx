using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The sidebar draws its icons from one SVG sprite (_NavIcons): every icon a nav item uses must be defined there,
/// or the item shows an empty square.
/// </summary>
[Collection(HostCollection.Name)]
public sealed partial class NavIconsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task EveryNavIcon_IsDefinedInTheSprite()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        await host.OwnerAsync();
        var owner = new Browser(host);
        Assert.Equal(HttpStatusCode.Redirect,
            (await owner.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword })).StatusCode);

        var html = await (await owner.GetAsync("/Library")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var used = UsedIcon().Matches(html).Select(m => m.Groups[1].Value).ToHashSet();
        var defined = DefinedIcon().Matches(html).Select(m => m.Groups[1].Value).ToHashSet();

        Assert.True(used.Count >= 15, $"the owner's nav should draw its icons from the sprite (found {used.Count})");
        Assert.Empty(used.Except(defined));
        Assert.Contains("i-puzzle", used); // Plugins
        Assert.DoesNotContain("<span class=\"nav-icon\">&#x", html);
    }

    [GeneratedRegex("<use href=\"#(i-[a-z-]+)\"")]
    private static partial Regex UsedIcon();

    [GeneratedRegex("<symbol id=\"(i-[a-z-]+)\"")]
    private static partial Regex DefinedIcon();
}
