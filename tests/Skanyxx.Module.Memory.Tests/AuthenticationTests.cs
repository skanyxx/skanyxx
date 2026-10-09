using System.Net;
using System.Net.Http.Json;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>REST identity is the signed-in principal only; the old X-User-Id header neither signs in nor overrides it.</summary>
public sealed class AuthenticationTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/api/memory/cards?q=refund" },
        { "GET", "/api/memory/cards/company/k" },
        { "PUT", "/api/memory/cards/personal:ana/k" },
        { "POST", "/api/memory/cards/personal:ana/k/lift" },
        { "GET", "/api/memory/grants/seed" },
        { "PUT", "/api/memory/grants/seed" }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task UserIdHeaderAlone_IsUnauthorized(string method, string path)
    {
        var client = App.Client();
        client.DefaultRequestHeaders.Add("X-User-Id", MemoryApp.Supervisor);
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "GET")
            request.Content = JsonContent.Create(new { version = 0, type = "fact", what = "w", why = "y", targetScope = "company", grants = Array.Empty<object>() });

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task UserIdHeader_DoesNotOverrideTheSignedInUser()
    {
        var ana = App.Client("ana");
        ana.DefaultRequestHeaders.Add("X-User-Id", "bob");

        var asBob = await ana.PutCardAsync("personal:bob", "k");
        var own = await ana.PutCardAsync("personal:ana", "k");

        Assert.Equal(HttpStatusCode.Forbidden, asBob.StatusCode);
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        Assert.Equal("ana", (await own.CardAsync()).Who);
    }

    [Theory]
    [InlineData(SkanyxxRoles.Owner, HttpStatusCode.OK)]
    [InlineData(SkanyxxRoles.Supervisor, HttpStatusCode.OK)]
    [InlineData(SkanyxxRoles.Employee, HttpStatusCode.Forbidden)]
    [InlineData(SkanyxxRoles.Builder, HttpStatusCode.Forbidden)]
    public async Task Role_DecidesSupervisorRights(string role, HttpStatusCode expected)
    {
        var client = App.Client("olga", role);

        var grants = await client.SetGrantsAsync("seed", new { scope = "company", canSearch = true, canUpsert = false });
        var company = await client.PutCardAsync("company", "k");

        Assert.Equal(expected, grants.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? HttpStatusCode.Created : HttpStatusCode.Forbidden, company.StatusCode);
    }

    [Fact]
    public async Task SupervisorByName_WithoutRole_IsNotASupervisor()
    {
        var response = await App.Client(MemoryApp.Supervisor).PutCardAsync("company", "k");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // kagent has no signed-in user: /mcp/memory answers on an agent secret (D080) while the REST API needs sign-in.
    [Fact]
    public async Task Mcp_WorksWithoutASignedInUser_WhileRestIsUnauthorized()
    {
        await using var mcp = await App.McpAsync("seed", userId: Users.Ana);

        var upsert = await mcp.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "k", ["type"] = "fact", ["what"] = "w", ["why"] = "y"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var rest = await App.Client().GetAsync("/api/memory/cards?q=w", TestContext.Current.CancellationToken);

        Assert.NotEqual(true, upsert.IsError);
        Assert.Equal(1, await Postgres.CardCountAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);
    }
}
