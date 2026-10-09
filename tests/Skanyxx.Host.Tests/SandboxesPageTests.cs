using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The Sandboxes page (Experimental, Google AX): owner and supervisor only, a shell whose script calls
/// <c>api/sandboxes</c>. The module's own rules are covered in its tests; here only who sees the page and what it says.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class SandboxesPageTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string Image = "registry.example/sandbox-runner";

    private string _database = null!;

    public async ValueTask InitializeAsync() => _database = await fixture.NewDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task OwnerAndSupervisor_SeeTheExperimentalPage_InTheirNav()
    {
        await using var host = await HostApp.StartAsync(_database, s =>
        {
            s["Sandboxes:AllowedImages:0"] = Image;
            s["Sandboxes:NetworkIsolationConfirmed"] = "true";
        });
        var ownerApi = await host.OwnerAsync();
        await PersonAsync(host, ownerApi, "sue@skanyxx.example", "supervisor");

        foreach (var (email, password) in new[] { (HostApp.OwnerEmail, HostApp.OwnerPassword), ("sue@skanyxx.example", Password) })
        {
            var browser = await SignInAsync(host, email, password);
            var page = await browser.GetAsync("/Sandboxes");
            var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("Experimental", html);
            Assert.Contains("href=\"/Sandboxes\"", html);
            Assert.Contains("id=\"run-form\"", html);
            Assert.Contains($"<option value=\"{Image}\">", html);
            // A prefix is a suggestion, never a ready-made value: it is not a runnable reference on its own.
            Assert.Contains("<input id=\"run-image\" required list=\"allowed-images\" autocomplete=\"off\" />", html);
            // Browsers compile pattern with the v flag; an unescaped "-" in a class makes it invalid and unenforced.
            Assert.Contains("pattern=\"[a-z0-9]([a-z0-9\\-]*[a-z0-9])?\"", html);
            Assert.Contains("'/api/sandboxes/tasks'", html);
            Assert.DoesNotContain("sandboxes-off", html);
            // API values are written with textContent only.
            Assert.DoesNotContain("innerHTML", html);
        }
    }

    [Fact]
    public async Task AnEmployee_GetsNoPage_AndNoNavItem()
    {
        await using var host = await HostApp.StartAsync(_database);
        var ownerApi = await host.OwnerAsync();
        await PersonAsync(host, ownerApi, "emma@skanyxx.example", "employee");
        var emma = await SignInAsync(host, "emma@skanyxx.example", Password);

        var page = await emma.GetAsync("/Sandboxes");
        var library = await (await emma.GetAsync("/Library")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The cookie scheme's access-denied path is the sign-in page.
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/Login", new Uri(host.BaseAddress, page.Headers.Location!).AbsolutePath);
        Assert.DoesNotContain("id=\"run-form\"", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("href=\"/Sandboxes\"", library);
    }

    [Fact]
    public async Task Anonymous_IsSentToSignIn()
    {
        await using var host = await HostApp.StartAsync(_database);

        var page = await host.Client().GetAsync("/Sandboxes", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/Login", new Uri(host.BaseAddress, page.Headers.Location!).AbsolutePath);
    }

    [Fact]
    public async Task ModuleOff_ThePageSaysSo_AndCallsNothing()
    {
        await using var host = await HostApp.StartAsync(_database, HostApp.WithoutSandboxes);
        await host.OwnerAsync();
        var owner = await SignInAsync(host, HostApp.OwnerEmail, HostApp.OwnerPassword);

        var html = await (await owner.GetAsync("/Sandboxes")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("id=\"sandboxes-off\"", html);
        Assert.Contains("Sandboxes:Enabled", html);
        Assert.DoesNotContain("/api/sandboxes", html);
        Assert.DoesNotContain("id=\"run-form\"", html);
    }

    private static async Task<Browser> SignInAsync(HostApp host, string email, string password)
    {
        var browser = new Browser(host);
        var signedIn = await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        return browser;
    }

    private static async Task PersonAsync(HostApp host, HttpClient ownerApi, string email, string role)
    {
        var invite = await ownerApi.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }
}
