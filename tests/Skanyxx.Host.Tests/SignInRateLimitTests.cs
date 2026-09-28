using System.Net.Http.Json;

namespace Skanyxx.Host.Tests;

/// <summary>Password guessing is capped per IP in its own window, through the API and through the login form alike.</summary>
[Collection(HostCollection.Name)]
public sealed class SignInRateLimitTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ApiAndFormPosts_ShareTheSignInWindow_ReadsDoNot()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "2";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
        });
        var credentials = new { email = HostApp.OwnerEmail, password = "wrong password" };

        var api = await host.Client().PostAsJsonAsync("/api/identity/sign-in", credentials);
        var form = await host.Client().PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var third = await host.Client().PostAsJsonAsync("/api/identity/sign-in", credentials);
        var setupForm = await host.Client().PostAsync("/Setup", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var status = await host.Client().GetAsync("/api/identity/status");

        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, form.StatusCode); // no antiforgery token, but it still counted
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, setupForm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }
}
