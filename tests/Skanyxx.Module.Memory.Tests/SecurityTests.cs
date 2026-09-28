using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class SecurityTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task Identity_InQueryString_IsIgnored()
    {
        await App.SupervisorClient().PutCardAsync("personal:boss", "secret");

        var read = await App.Client().GetAsync("/api/memory/cards/personal:boss/secret?userId=boss");
        var search = await App.Client().GetAsync("/api/memory/cards?q=refund&userId=boss");

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, search.StatusCode);
    }

    [Fact]
    public async Task Identity_InJsonBody_IsIgnored()
    {
        var anonymous = await App.Client().PutAsJsonAsync("/api/memory/cards/company/k1",
            new { userId = MemoryApp.Supervisor, version = 0, type = "fact", what = "w", why = "y" });
        var asAna = await App.Client("ana").PutAsJsonAsync("/api/memory/cards/company/k1",
            new { userId = MemoryApp.Supervisor, version = 0, type = "fact", what = "w", why = "y" });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asAna.StatusCode);
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task QueryString_CannotRetargetRouteOrBody()
    {
        var boss = App.SupervisorClient();
        await boss.PutCardAsync("personal:boss", "secret", body: "BOSS-ONLY");
        await App.Client("ana").PutCardAsync("personal:ana", "note");

        var read = await boss.GetAsync("/api/memory/cards/company/k?scope=personal:boss&key=secret");
        var put = await App.Client("ana").PutCardAsync("personal:ana", "note?version=0&scope=company&key=other", version: 1, what: "v2");
        var grants = await boss.SetGrantsAsync("a?agentId=b", new { scope = "company", canSearch = true, canUpsert = false });
        var lift = await App.Client("ana").PostAsJsonAsync(
            "/api/memory/cards/personal:ana/note/lift?targetScope=company", new { targetScope = "team:t" });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("personal:ana", (await put.CardAsync()).Scope);
        Assert.Equal(HttpStatusCode.OK, grants.StatusCode);
        Assert.Equal("team:t", (await lift.CardAsync()).Scope);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(["a"], db.Grants.Select(g => g.AgentId).ToList());
        Assert.DoesNotContain(db.Cards, c => c.Scope == "company");
    }

    [Fact]
    public async Task NulCharacters_AreAValidationError_NotA500()
    {
        var write = await App.Client("ana").PutCardAsync("personal:ana", "nul", what: "a\0b");
        var search = await App.Client("ana").GetAsync("/api/memory/cards?q=a%00b");

        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, search.StatusCode);
    }

    [Fact]
    public async Task OversizedBody_Rejected()
    {
        var response = await App.Client("ana").PutCardAsync("personal:ana", "big", body: new string('b', 300_000));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    [Theory]
    [InlineData('ש')] // Hebrew: JSON-escaped as \uXXXX, 6 bytes per character
    [InlineData('語')] // CJK
    public async Task MaxSizeNonAsciiCard_FitsTheBodyLimit(char letter)
    {
        var response = await App.Client("ana").PutCardAsync("personal:ana", "max",
            what: new string(letter, 200), why: new string(letter, 400), body: new string(letter, 20_000), source: new string(letter, 2_048));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OversizedMcpBody_Rejected_BeforeParsing()
    {
        var huge = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"pad\":\"" + new string('x', 300_000) + "\"}",
            System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/memory") { Content = huge };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new("Bearer", await App.IssueSecretAsync("seed"));

        var response = await App.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_IsProblemDetails()
    {
        var response = await App.Client("ana").PutAsync("/api/memory/cards/personal:ana/k",
            new StringContent("{", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, (await response.JsonAsync()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task BindingErrors_HaveTheValidationShape_AndDoNotEchoInput()
    {
        var response = await App.Client("ana").PutAsync("/api/memory/cards/personal:ana/k", new StringContent(
            "{\"version\":\"<script>alert(1)</script>\",\"type\":\"fact\",\"what\":\"w\",\"why\":\"y\"}",
            System.Text.Encoding.UTF8, "application/json"));
        var grants = await App.SupervisorClient().PutAsync("/api/memory/grants/seed",
            new StringContent("{\"grants\":\"not-a-list\"}", System.Text.Encoding.UTF8, "application/json"));

        foreach (var r in new[] { response, grants })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync();
            var json = System.Text.Json.JsonDocument.Parse(body).RootElement;
            Assert.Equal(System.Text.Json.JsonValueKind.Object, json.GetProperty("errors").ValueKind);
            Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
            Assert.DoesNotContain("<script>", body);
            Assert.DoesNotContain("Skanyxx.", body);
        }
    }

    [Theory]
    [InlineData("personal:ana\n", "k")]
    [InlineData("personal:ana", "k\n")]
    public async Task TrailingNewline_InScopeOrKey_IsAValidationError(string scope, string key)
    {
        var response = await App.Client("ana").PutCardAsync(Uri.EscapeDataString(scope), Uri.EscapeDataString(key));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ApiFailure_ReturnsProblemDetails_WithoutInternals()
    {
        var response = await App.Client("ana").GetAsync(MemoryApp.ApiFailurePath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("internal detail", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/memory/cards", "GET")]
    [InlineData("/api/memory/cards/company/k1", "PUT")]
    [InlineData("/mcp/memory", "POST")]
    public async Task MemoryEndpoints_OptOutOfTheGlobalCorsPolicy(string path, string method)
    {
        var control = await PreflightAsync(MemoryApp.CorsControlPath, "GET");
        var memory = await PreflightAsync(path, method);

        Assert.True(control.Headers.Contains("Access-Control-Allow-Origin"), "control endpoint should get CORS headers");
        Assert.False(memory.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private Task<HttpResponseMessage> PreflightAsync(string path, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "x-user-id");
        return App.Client().SendAsync(request);
    }
}
