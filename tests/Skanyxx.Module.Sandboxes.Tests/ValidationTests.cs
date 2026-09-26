using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class ValidationTests : SandboxesTestBase
{
    [Theory]
    [InlineData("Upper")]
    [InlineData("-lead")]
    [InlineData("trail-")]
    [InlineData("has_underscore")]
    [InlineData("has.dot")]
    [InlineData("a234567890123456789012345678901234567890123456789012345678901234")] // 64
    [InlineData("new%0Aline")]
    public async Task NonDnsLabelNames_Are400_AndNeverReachAx(string name)
    {
        var run = await RunAsync(name);
        var get = await App.Client().GetAsync($"/api/sandboxes/tasks/{name}");
        var stop = await App.Client().PostAsync($"/api/sandboxes/tasks/{name}/stop", null);

        Assert.Equal(HttpStatusCode.BadRequest, run.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, get.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, stop.StatusCode);
        Assert.Empty(Ax.Atespaces);
    }

    [Fact]
    public async Task A63CharLabel_IsFine() =>
        Assert.Equal(HttpStatusCode.Accepted, (await RunAsync(new string('a', 63))).StatusCode);

    [Theory]
    [InlineData("SKANYXX_OWNER")]
    [InlineData("SKANYXX_USER_ID")]
    [InlineData("SKANYXX_AGENT_ID")]
    [InlineData("skanyxx_owner")]
    [InlineData("SKANYXX_ANYTHING")]
    public async Task ReservedEnv_CannotBeSetByTheCaller(string key)
    {
        var response = await RunAsync("fix-42", body: RunBody(env: new Dictionary<string, string> { [key] = "boss" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Ax.Updates);
    }

    public static TheoryData<string, object> BadBodies => new()
    {
        { "no image", new { command = new[] { "x" } } },
        { "null image", new { image = (string?)null } },
        { "image with space", new { image = "ghcr.io/a b" } },
        { "bad env name", new { image = "ghcr.io/acme/img", env = new Dictionary<string, string> { ["1X"] = "v" } } },
        { "NUL in command", new { image = "ghcr.io/acme/img", command = new[] { "a\0b" } } },
        { "too many args", new { image = "ghcr.io/acme/img", command = Enumerable.Repeat("a", 65).ToArray() } },
        { "workspace name", new { image = "ghcr.io/acme/img", workspaces = new[] { new { name = "Repo" } } } },
        { "workspace path escape", new { image = "ghcr.io/acme/img", workspaces = new[] { new { name = "repo", path = "/workspace/../etc" } } } },
        { "workspace path outside", new { image = "ghcr.io/acme/img", workspaces = new[] { new { name = "repo", path = "/etc" } } } },
        { "bad quantity", new { image = "ghcr.io/acme/img", resources = new { limits = new { cpu = "lots" } } } }
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task BadRunBodies_Are400(string why, object body)
    {
        var response = await RunAsync("fix-42", body: body);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, why);
        Assert.Empty(Ax.Updates);
    }

    [Fact]
    public async Task UnknownFields_LikeDebug_AreIgnored()
    {
        await App.Client().PutAsJsonAsync("/api/sandboxes/tasks/fix-42", new { image = "ghcr.io/acme/img", debug = true });

        Assert.False(Assert.Single(Ax.Updates).Task.Spec.Debug);
    }

    [Fact]
    public async Task QueryString_CannotRetargetTheRoute()
    {
        await App.Client().PutAsJsonAsync("/api/sandboxes/tasks/fix-42?name=other", new { image = "ghcr.io/acme/img" });

        Assert.Equal("fix-42", Assert.Single(Ax.Updates).Task.Metadata.Name);
    }

    [Theory]
    [InlineData("GET", "/api/sandboxes/tasks")]
    [InlineData("GET", "/api/sandboxes/tasks/fix-42")]
    [InlineData("PUT", "/api/sandboxes/tasks/fix-42")]
    [InlineData("POST", "/api/sandboxes/tasks/fix-42/stop")]
    [InlineData("POST", "/api/sandboxes/tasks/fix-42/suspend")]
    [InlineData("POST", "/api/sandboxes/tasks/fix-42/resume")]
    [InlineData("GET", "/api/sandboxes/tasks/fix-42/watch")]
    [InlineData("GET", "/api/sandboxes/workspaces")]
    [InlineData("PUT", "/api/sandboxes/workspaces/ws")]
    [InlineData("GET", "/api/sandboxes/models")]
    public async Task EveryEndpoint_RequiresAValidUser(string method, string path)
    {
        HttpRequestMessage Request(string? user)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { image = "ghcr.io/acme/img" }) };
            if (user is not null)
                request.Headers.Add("X-User-Id", user);
            return request;
        }

        var anonymous = await App.Client(userId: null).SendAsync(Request(null));
        var invalid = await App.Client(userId: null).SendAsync(Request("Ana Smith"));

        Assert.Equal(HttpStatusCode.BadRequest, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(Ax.Atespaces);
    }

    [Fact]
    public async Task Cors_IsOff()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/sandboxes/tasks");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await App.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OversizedBody_IsRejected()
    {
        var huge = new { image = "ghcr.io/acme/img", command = new[] { new string('a', 3 * 1024 * 1024) } };

        var response = await RunAsync("fix-42", body: huge);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
