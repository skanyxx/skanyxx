using System.Net;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

/// <summary>SEC H1: what may run (image allowlist, digest) and how big (resource bounds and defaults).</summary>
public sealed class PolicyTests : SandboxesTestBase
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static object Body(string image, object? resources = null) =>
        new { image, command = new[] { "x" }, resources };

    [Theory]
    [InlineData("docker.io/evil/miner:latest")]
    [InlineData("ghcr.io/acme-evil/x:1")]
    [InlineData("ghcr.io/acmex")]
    [InlineData("ghcr.io/acme/../evil/x")]
    [InlineData("evil.example/ghcr.io/acme/x")]
    public async Task ImagesOutsideTheAllowlist_Are400(string image)
    {
        var response = await RunAsync("fix-42", body: Body(image));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Ax.Updates);
    }

    [Fact]
    public async Task AnEmptyAllowlist_RunsNothing()
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:AllowedImages:0"] = null);

        var response = await RunAsync("fix-42", body: Body("ghcr.io/acme/agent:1"), app: app);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Ax.Updates);
    }

    [Theory]
    [InlineData("ghcr.io/acme/agent", HttpStatusCode.Accepted)]
    [InlineData("ghcr.io/acme/agent:1", HttpStatusCode.Accepted)]
    [InlineData("ghcr.io/acme/agent/sub:1", HttpStatusCode.Accepted)]
    [InlineData("ghcr.io/acme/agent-evil:1", HttpStatusCode.BadRequest)]
    [InlineData("ghcr.io/acme/agentx", HttpStatusCode.BadRequest)]
    public async Task APrefixWithoutASlash_EndsAtAPathTagOrDigestBoundary(string image, HttpStatusCode expected)
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:AllowedImages:0"] = "ghcr.io/acme/agent");

        Assert.Equal(expected, (await RunAsync("fix-42", body: Body(image), app: app)).StatusCode);
    }

    // SEC2 N4: after a host-only prefix, ':' is a registry port unless nothing but a tag follows it.
    [Theory]
    [InlineData("acme", "acme:5000/evil/x", HttpStatusCode.BadRequest)]
    [InlineData("localhost", "localhost:5000/evil/x", HttpStatusCode.BadRequest)]
    [InlineData("ghcr.io", "ghcr.io:5000/evil/x", HttpStatusCode.BadRequest)]
    [InlineData("acme", "acme:1", HttpStatusCode.Accepted)]
    [InlineData("ghcr.io", "ghcr.io/acme/agent:1", HttpStatusCode.Accepted)]
    public async Task AColonAfterThePrefix_IsATagOnly_NeverARegistryPort(string prefix, string image, HttpStatusCode expected)
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:AllowedImages:0"] = prefix);

        Assert.Equal(expected, (await RunAsync("fix-42", body: Body(image), app: app)).StatusCode);
    }

    [Theory]
    [InlineData("ghcr.io/acme/agent:1", HttpStatusCode.BadRequest)]
    [InlineData("ghcr.io/acme/agent@sha256:0123abcd", HttpStatusCode.BadRequest)]
    [InlineData("ghcr.io/acme/agent@sha256:" + Digest, HttpStatusCode.Accepted)]
    public async Task RequireDigest_AdmitsOnlyPinnedImages(string image, HttpStatusCode expected)
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:RequireDigest"] = "true");

        Assert.Equal(expected, (await RunAsync("fix-42", body: Body(image), app: app)).StatusCode);
    }

    public static TheoryData<string, object> TooMuch => new()
    {
        { "cpu limit over max", new { limits = new { cpu = "100000" } } },
        { "cpu request over max", new { requests = new { cpu = "4001m" } } },
        { "memory request over max", new { requests = new { memory = "1000Ti" } } },
        { "memory limit over max", new { limits = new { memory = "9Gi" } } },
        { "request above limit", new { requests = new { cpu = "2" }, limits = new { cpu = "1" } } },
        { "not a quantity", new { requests = new { memory = "" } } },
        { "too many digits", new { limits = new { cpu = "1234567890123" } } }
    };

    [Theory]
    [MemberData(nameof(TooMuch))]
    public async Task ResourcesBeyondTheBounds_Are400(string why, object resources)
    {
        var response = await RunAsync("fix-42", body: Body("ghcr.io/acme/agent:1", resources));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, why);
        Assert.Empty(Ax.Updates);
    }

    [Fact]
    public async Task ResourcesAtTheBounds_AreFine() =>
        Assert.Equal(HttpStatusCode.Accepted, (await RunAsync("fix-42",
            body: Body("ghcr.io/acme/agent:1", new { requests = new { cpu = "4", memory = "8Gi" }, limits = new { cpu = "4000m", memory = "8192Mi" } })))
            .StatusCode);

    [Fact]
    public async Task MissingResources_GetTheDefaults()
    {
        await RunAsync("fix-42", body: Body("ghcr.io/acme/agent:1"));

        var resources = Assert.Single(Ax.Updates).Task.Spec.Resources;
        Assert.Equal(("250m", "256Mi"), (resources.Requests.Cpu, resources.Requests.Memory));
        Assert.Equal(("1", "1Gi"), (resources.Limits.Cpu, resources.Limits.Memory));
    }

    [Fact]
    public async Task AMissingLimit_IsNeverBelowTheRequest_AndAMissingRequestNeverAboveTheLimit()
    {
        await RunAsync("fix-42", body: Body("ghcr.io/acme/agent:1",
            new { requests = new { cpu = "2" }, limits = new { memory = "128Mi" } }));

        var resources = Assert.Single(Ax.Updates).Task.Spec.Resources;
        Assert.Equal(("2", "128Mi"), (resources.Requests.Cpu, resources.Requests.Memory));
        Assert.Equal(("2", "128Mi"), (resources.Limits.Cpu, resources.Limits.Memory));
    }
}
