using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// SEC M2 end to end: a supervisor mints an agent's memory secret; once the owner demotes or disables them, that secret
/// is refused on <c>/mcp/memory</c>. The owner's own (acts-for-users) secret is untouched.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class OffboardingTests(PostgresFixture fixture)
{
    private const string SupervisorEmail = "sam@skanyxx.example";
    private const string SupervisorPassword = "a long supervisor passphrase";

    [Theory]
    [InlineData("roles")]
    [InlineData("disable")]
    public async Task SecretsASupervisorIssued_StopWorking_WhenTheyAreDemotedOrDisabled(string change)
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        var owner = await host.OwnerAsync();
        var (supervisor, supervisorId) = await SupervisorAsync(host, owner);
        var minted = await IssueAsync(supervisor, "sam-agent", actsForUsers: false);
        var ownersSecret = await IssueAsync(owner, "owner-agent", actsForUsers: true);
        var before = await host.Client(minted).SendAsync(ToolsList());

        var changed = change == "roles"
            ? await owner.PutAsJsonAsync($"/api/identity/people/{supervisorId}/roles", new { roles = new[] { "employee" } })
            : await owner.PostAsync($"/api/identity/people/{supervisorId}/disable", null);
        var after = await host.Client(minted).SendAsync(ToolsList());
        var owners = await host.Client(ownersSecret).SendAsync(ToolsList());

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(HttpStatusCode.OK, owners.StatusCode);
    }

    /// <summary>
    /// SEC2 N1: a demoted or disabled supervisor's browser cookie is refused on its very next request, API or page, so
    /// it cannot mint an agent secret after the revocation ran (the cookie used to keep its roles for up to 60 s).
    /// </summary>
    [Theory]
    [InlineData("roles")]
    [InlineData("disable")]
    public async Task ASupervisorsCookie_IsRefusedOnTheVeryNextRequest_AfterDemotionOrDisable(string change)
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        var owner = await host.OwnerAsync();
        var (_, supervisorId) = await SupervisorAsync(host, owner);
        var signIn = await host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = SupervisorEmail, password = SupervisorPassword, useCookie = true });
        var cookie = "skanyxx.auth=" + signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("skanyxx.auth=")).Split(';')[0]["skanyxx.auth=".Length..];
        var before = await WithCookie(host, cookie, HttpMethod.Post, "/api/memory/agents/before/secret");

        var changed = change == "roles"
            ? await owner.PutAsJsonAsync($"/api/identity/people/{supervisorId}/roles", new { roles = new[] { "employee" } })
            : await owner.PostAsync($"/api/identity/people/{supervisorId}/disable", null);
        var mint = await WithCookie(host, cookie, HttpMethod.Post, "/api/memory/agents/after/secret");
        var page = await WithCookie(host, cookie, HttpMethod.Get, "/Hooks");
        var status = await owner.GetFromJsonAsync<JsonElement>("/api/memory/agents/after/secret");

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, mint.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Login", page.Headers.Location?.OriginalString);
        Assert.False(status.GetProperty("hasSecret").GetBoolean());
    }

    private static Task<HttpResponseMessage> WithCookie(HostApp host, string cookie, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path) { Content = method == HttpMethod.Post ? JsonContent.Create(new { actsForUsers = false }) : null };
        request.Headers.Add("Cookie", cookie);
        return host.Client().SendAsync(request);
    }

    private static async Task<(HttpClient Client, string Id)> SupervisorAsync(HostApp host, HttpClient owner)
    {
        var invite = await owner.PostAsJsonAsync("/api/identity/invites", new { email = SupervisorEmail, roles = new[] { "supervisor" } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = SupervisorPassword });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        return (host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!), body.GetProperty("user").GetProperty("id").GetString()!);
    }

    private static async Task<string> IssueAsync(HttpClient issuer, string agentId, bool actsForUsers)
    {
        var issued = await issuer.PostAsJsonAsync($"/api/memory/agents/{agentId}/secret", new { actsForUsers });
        issued.EnsureSuccessStatusCode();
        return (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
    }

    private static HttpRequestMessage ToolsList()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/memory")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }
}
