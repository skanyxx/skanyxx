using System.Net.Http.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// requirements.md O2 (D272): Microsoft sign-in is off and hidden unless the install's switch
/// (<c>Identity:EntraAvailable</c>) is on. Off, even the owner gets no nav item, page, settings API or login button.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class EntraSwitchTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HostApp _host = null!;

    public async ValueTask InitializeAsync() => _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task SwitchOff_TheOwnerGetsNoEntraNavPageOrApi()
    {
        var api = await _host.OwnerAsync();
        var browser = new Browser(_host);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword })).StatusCode);

        var nav = await (await browser.GetAsync("/Library")).Content.ReadAsStringAsync(Ct);
        var page = await browser.GetAsync("/Entra");
        var settings = await api.GetAsync("/api/identity/entra/settings", Ct);
        var save = await api.PutAsJsonAsync("/api/identity/entra/settings", new { enabled = true, tenantId = Guid.NewGuid(), clientId = Guid.NewGuid(), groups = Array.Empty<object>() }, Ct);
        var unlink = await api.DeleteAsync($"/api/identity/people/{Guid.NewGuid()}/entra-login", Ct);

        Assert.Contains("href=\"/People\"", nav);
        Assert.DoesNotContain("href=\"/Entra\"", nav);
        Assert.DoesNotContain("Microsoft sign-in", nav);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settings.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unlink.StatusCode);
    }

    [Fact]
    public async Task SwitchOff_TheLoginPageHasNoMicrosoftButton()
    {
        await _host.OwnerAsync();

        var login = await (await new Browser(_host).GetAsync("/Login")).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain("Microsoft", login);
    }
}
