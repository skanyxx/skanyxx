using System.Net.Http.Json;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>Administration is the owner's: other roles get 403, anonymous callers 401. Lookup and accept are anonymous and rate limited in their own window.</summary>
[Collection(PostgresCollection.Name)]
public sealed class InviteAccessTests(PostgresFixture postgres)
{
    public static TheoryData<string, string> OwnerRoutes => new()
    {
        { "POST", "/api/identity/invites" },
        { "GET", "/api/identity/invites" },
        { "DELETE", "/api/identity/invites/00000000-0000-0000-0000-000000000000" },
        { "GET", "/api/identity/people" },
        { "PUT", "/api/identity/people/{member}/roles" },
        { "POST", "/api/identity/people/{member}/disable" },
        { "POST", "/api/identity/people/{member}/enable" }
    };

    [Theory]
    [MemberData(nameof(OwnerRoutes))]
    public async Task OwnerRoutes_Are403ForOtherRoles_And401Anonymous(string method, string route)
    {
        await using var app = await StartAsync();
        var owner = (await app.SignInBearerAsync()).AccessToken;
        var (supervisor, supervisorId) = await app.AddMemberAsync(owner, "supervisor@skanyxx.example",
            SkanyxxRoles.Supervisor, SkanyxxRoles.Builder, SkanyxxRoles.Employee);
        var path = route.Replace("{member}", supervisorId);

        var asSupervisor = await app.Client(bearer: supervisor.AccessToken).SendAsync(Request(method, path));
        var anonymous = await app.Client().SendAsync(Request(method, path));
        var people = await app.Client(bearer: owner).GetFromJsonAsync<System.Text.Json.JsonElement>("/api/identity/people");
        var target = people.EnumerateArray().Single(p => p.GetProperty("id").GetString() == supervisorId);

        Assert.Equal(HttpStatusCode.Forbidden, asSupervisor.StatusCode);
        Assert.Equal("application/problem+json", asSupervisor.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.False(target.GetProperty("disabled").GetBoolean());
        Assert.Equal(["builder", "employee", "supervisor"], target.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    /// <summary>SEC L8: their own window, so crawlers opening invite links cannot spend the one sign-in depends on.</summary>
    [Fact]
    public async Task LookupAndAccept_AreAnonymous_AndCountedInTheirOwnWindow()
    {
        await using var app = await StartAsync(s =>
        {
            s["Skanyxx:InviteRateLimit:PermitLimit"] = "3";
            s["Skanyxx:InviteRateLimit:WindowSeconds"] = "600";
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "3";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
        });
        // Sign-in window: 1 was the bootstrap.
        var owner = (await app.SignInBearerAsync()).AccessToken; // sign-in 2
        var token = await app.InviteAsync(owner);                  // owner route: neither window

        var lookup = await app.LookupInviteAsync(token);            // invite 1
        var accept = await app.AcceptAsync(token);                  // invite 2
        var guess = await app.LookupInviteAsync("skx_inv_guess");   // invite 3
        var limited = await app.LookupInviteAsync("skx_inv_guess"); // invite 4: over
        var signIn = await app.SignInAsync();                        // sign-in 3: its window untouched
        var list = await app.Client(bearer: owner).GetAsync("/api/identity/invites");

        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.Created, accept.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, guess.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task AnExhaustedSignInWindow_LeavesInvitesWorking()
    {
        await using var app = await StartAsync(s =>
        {
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "2";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
        });
        var owner = (await app.SignInBearerAsync()).AccessToken; // bootstrap + this = 2
        var token = await app.InviteAsync(owner);

        var limited = await app.SignInAsync();
        var lookup = await app.LookupInviteAsync(token);
        var accept = await app.AcceptAsync(token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.Created, accept.StatusCode);
    }

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(new { email = "x@skanyxx.example", roles = new[] { "employee" } }) : null
        };

    private async Task<IdentityApp> StartAsync(Action<Dictionary<string, string?>>? configure = null)
    {
        await postgres.ResetAsync();
        var app = await IdentityApp.StartAsync(postgres.ConnectionString, configure);
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        return app;
    }
}
