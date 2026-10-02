using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>D080: per-agent secrets — issue, rotate, revoke, status — and <c>/mcp/memory</c> accepting nothing else.</summary>
public sealed class AgentSecretTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private const string Path = "/api/memory/agents/seed/secret";
    private const string UnknownSecret = "skx_mem_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
        (result.StructuredContent is { } s ? System.Text.Json.JsonSerializer.Serialize(s) : "");

    [Fact]
    public async Task Issue_ReturnsAPrefixed256BitSecret_Once()
    {
        var response = await App.SupervisorClient().PostAsync(Path, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.JsonAsync();
        Assert.Equal("seed", json.GetProperty("agentId").GetString());
        var secret = json.GetProperty("secret").GetString()!;
        Assert.StartsWith("skx_mem_", secret);
        Assert.Equal(32, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(secret["skx_mem_".Length..]).Length);
        Assert.NotEqual(secret, await App.IssueSecretAsync("other"));
    }

    [Fact]
    public async Task Status_ReportsTheSecret_WithoutEverReturningIt()
    {
        var before = await (await App.SupervisorClient().GetAsync(Path)).JsonAsync();
        var issued = await (await App.SupervisorClient().PostAsync(Path, null)).JsonAsync();

        var status = await App.SupervisorClient().GetAsync(Path);

        Assert.False(before.GetProperty("hasSecret").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, before.GetProperty("createdBy").ValueKind);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var body = await status.Content.ReadAsStringAsync();
        var json = await status.JsonAsync();
        Assert.True(json.GetProperty("hasSecret").GetBoolean());
        Assert.Equal(issued.GetProperty("createdAt").GetDateTime(), json.GetProperty("createdAt").GetDateTime());
        Assert.DoesNotContain(issued.GetProperty("secret").GetString()!, body);
        Assert.DoesNotContain("secret\"", body.Replace("hasSecret\"", ""));
        // Who issued it, so an offboarding whose automatic revocation failed can be finished by hand (SEC2 N2).
        Assert.Equal(MemoryApp.Supervisor, json.GetProperty("createdBy").GetString());
    }

    /// <summary>SEC2 N4: the one record carrying the plaintext secret never prints it, alone or inside an Outcome.</summary>
    [Fact]
    public void IssuedSecret_ToString_NeverPrintsTheSecret()
    {
        const string secret = "skx_mem_THE-SECRET-VALUE";
        var issued = new Features.AgentSecrets.IssuedAgentSecret("seed", secret, DateTime.UnixEpoch, ActsForUsers: true);

        Assert.All(new object[] { issued, Outcome<Features.AgentSecrets.IssuedAgentSecret>.Ok(issued) }, printed =>
        {
            Assert.DoesNotContain(secret, printed.ToString());
            Assert.Contains("Secret = ***", printed.ToString());
            Assert.Contains("seed", printed.ToString());
        });
    }

    [Fact]
    public async Task Rotation_KillsTheOldSecret()
    {
        var old = await App.IssueSecretAsync("seed");
        var current = await App.IssueSecretAsync("seed");

        var withOld = await App.PostMcpAsync("tools/list", $"Bearer {old}");
        var withCurrent = await App.PostMcpAsync("tools/list", $"Bearer {current}");

        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withCurrent.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(1, await db.AgentSecrets.CountAsync());
    }

    [Fact]
    public async Task Revoke_LocksTheAgentOut()
    {
        var secret = await App.IssueSecretAsync("seed");

        var revoke = await App.SupervisorClient().DeleteAsync(Path);
        var mcp = await App.PostMcpAsync("tools/list", $"Bearer {secret}");
        var again = await App.SupervisorClient().DeleteAsync(Path);
        var status = await (await App.SupervisorClient().GetAsync(Path)).JsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.False(status.GetProperty("hasSecret").GetBoolean());
    }

    [Theory]
    [InlineData("POST", SkanyxxRoles.Employee)]
    [InlineData("GET", SkanyxxRoles.Employee)]
    [InlineData("DELETE", SkanyxxRoles.Employee)]
    [InlineData("POST", SkanyxxRoles.Builder)]
    public async Task NonSupervisor_IsForbidden(string method, string role)
    {
        await App.IssueSecretAsync("seed");

        var response = await App.Client("olga", role).SendAsync(new HttpRequestMessage(new HttpMethod(method), Path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("skx_mem_", await response.Content.ReadAsStringAsync());
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(1, await db.AgentSecrets.CountAsync());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task Anonymous_IsUnauthorized(string method)
    {
        var response = await App.Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), Path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Bad_Agent")]
    [InlineData("a%0A")]
    public async Task InvalidAgentId_IsAValidationError(string agentId)
    {
        var response = await App.SupervisorClient().PostAsync($"/api/memory/agents/{agentId}/secret", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string?> BadAuthorization => new()
    {
        null,
        "Basic c2VlZDpzZWVk",
        "Bearer",
        "Bearer ",
        "Bearer not-a-secret",
        $"Bearer {UnknownSecret}",
        $"Bearer {UnknownSecret}x",
        $"Bearer {UnknownSecret[..^1]}!"
    };

    [Theory]
    [MemberData(nameof(BadAuthorization))]
    public async Task Mcp_WithoutAValidSecret_Is401_ForInitializeAndToolsList(string? authorization)
    {
        await App.IssueSecretAsync("seed");

        foreach (var method in new[] { "initialize", "tools/list", "tools/call" })
        {
            var response = await App.PostMcpAsync(method, authorization, userId: "ana");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Mcp_WithASecret_AnswersInitializeAndToolsList()
    {
        var secret = await App.IssueSecretAsync("seed");

        var initialize = await App.PostMcpAsync("initialize", $"Bearer {secret}");
        var tools = await App.PostMcpAsync("tools/list", $"bearer {secret}");

        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tools.StatusCode);
        Assert.Contains("memory_search", await tools.Content.ReadAsStringAsync());
    }

    // Stateless MCP serves no standalone SSE stream (GET) and has no session to end (DELETE): MCP Streamable HTTP allows 405.
    // kagent's client sends both on every connection; they used to fall to the Host's user policy and get 401 with a
    // valid secret, which reads as a wrong secret in the log.
    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    public async Task Mcp_GetOrDelete_WithASecret_Is405(string method)
    {
        var secret = await App.IssueSecretAsync("seed");

        var response = await App.Client().SendAsync(McpRequest(method, $"Bearer {secret}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["POST"], response.Content.Headers.Allow);
    }

    [Theory]
    [InlineData("GET", null)]
    [InlineData("DELETE", null)]
    [InlineData("GET", "Bearer " + UnknownSecret)]
    [InlineData("DELETE", "Bearer " + UnknownSecret)]
    [InlineData("PUT", "Bearer " + UnknownSecret)]
    public async Task Mcp_GetOrDelete_WithoutAValidSecret_Is401(string method, string? authorization)
    {
        var response = await App.Client().SendAsync(McpRequest(method, authorization));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // The agent scheme's challenge, not the Host's user fallback (which also answers 401).
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    private static HttpRequestMessage McpRequest(string method, string? authorization)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/mcp/memory");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    // A signed-in human (the Host's cookie/bearer, here the test scheme) is not an agent.
    [Fact]
    public async Task Mcp_SignedInUserWithoutASecret_Is401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/memory")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await App.SupervisorClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgentComesFromTheSecret_ASpoofedAgentHeaderIsIgnored()
    {
        await App.SupervisorClient().SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        var reader = await App.IssueSecretAsync("reader", actsForUsers: true);
        await using var client = await App.McpWithSecretAsync(reader, userId: Users.Ana,
            headers: new Dictionary<string, string> { ["X-Agent-Id"] = "writer" });

        var company = await client.CallToolAsync("memory_upsert", Upsert("company"));
        var personal = await client.CallToolAsync("memory_upsert", Upsert("personal"));

        Assert.True(company.IsError);
        Assert.Contains("No upsert grant", Text(company));
        Assert.NotEqual(true, personal.IsError);
        Assert.Contains($"{Users.Ana} via reader", Text(personal));
        Assert.Equal(1, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task GrantsOfTheSecretsAgent_Apply()
    {
        await App.SupervisorClient().SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        await using var client = await App.McpAsync("writer");

        var company = await client.CallToolAsync("memory_upsert", Upsert("company"));

        Assert.NotEqual(true, company.IsError);
        Assert.Contains("agent:writer", Text(company));
    }

    [Fact]
    public async Task SecretIsStoredOnlyAsItsHash()
    {
        var secret = await App.IssueSecretAsync("seed");

        await using var db = Postgres.CreateDbContext();
        var row = await db.AgentSecrets.SingleAsync();
        var rowText = await db.Database.SqlQueryRaw<string>(
            "SELECT row_to_json(s)::text AS \"Value\" FROM memory_agent_secrets s").SingleAsync();

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), row.SecretHash);
        Assert.Equal(MemoryApp.Supervisor, row.CreatedBy);
        Assert.DoesNotContain(secret, rowText);
        Assert.DoesNotContain(secret["skx_mem_".Length..], rowText);
    }

    [Fact]
    public async Task Secrets_NeverReachTheLogs()
    {
        var secret = await App.IssueSecretAsync("seed", actsForUsers: true);
        await using (var client = await App.McpAsync("seed", userId: Users.Ana))
            await client.CallToolAsync("memory_upsert", Upsert("personal"));
        await App.PostMcpAsync("tools/list", $"Bearer {UnknownSecret}");
        await App.PostMcpAsync("tools/list", "Bearer not-a-secret-but-sensitive");
        await App.SupervisorClient().GetAsync(Path);

        var logs = App.Logs;

        Assert.Contains("MemoryAgentSecret", logs); // the scheme did log: this is not an empty capture
        Assert.DoesNotContain(secret, logs);
        Assert.DoesNotContain(secret["skx_mem_".Length..], logs);
        Assert.DoesNotContain(UnknownSecret, logs);
        Assert.DoesNotContain("not-a-secret-but-sensitive", logs);
    }

    // --- D084: acting for users is an owner-only, per-secret flag ---

    [Fact]
    public async Task ActsForUsers_BySupervisor_IsForbidden()
    {
        var response = await App.SupervisorClient().PostAsJsonAsync(Path, new { actsForUsers = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("skx_mem_", await response.Content.ReadAsStringAsync());
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(0, await db.AgentSecrets.CountAsync());
    }

    [Fact]
    public async Task ActsForUsers_ByOwner_IsSet_ReportedByStatus_AndEachIssueSetsItAgain()
    {
        var issued = await App.OwnerClient().PostAsJsonAsync(Path, new { actsForUsers = true });
        var status = await (await App.SupervisorClient().GetAsync(Path)).JsonAsync();
        var rotated = await App.OwnerClient().PostAsync(Path, null); // no body = off
        var after = await (await App.SupervisorClient().GetAsync(Path)).JsonAsync();

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.True((await issued.JsonAsync()).GetProperty("actsForUsers").GetBoolean());
        Assert.True(status.GetProperty("actsForUsers").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.False((await rotated.JsonAsync()).GetProperty("actsForUsers").GetBoolean());
        Assert.False(after.GetProperty("actsForUsers").GetBoolean());
    }

    // A supervisor may not rotate away or revoke what only the owner could set; the row is left exactly as it was.
    [Fact]
    public async Task FlaggedSecret_SupervisorRotate_IsForbidden_AndTheOldSecretStillWorks()
    {
        var owners = await App.IssueSecretAsync("seed", actsForUsers: true);

        var rotate = await App.SupervisorClient().PostAsync(Path, null);
        var mcp = await App.PostMcpAsync("tools/list", $"Bearer {owners}");

        Assert.Equal(HttpStatusCode.Forbidden, rotate.StatusCode);
        Assert.DoesNotContain("skx_mem_", await rotate.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, mcp.StatusCode);
        await using var db = Postgres.CreateDbContext();
        var row = await db.AgentSecrets.SingleAsync();
        Assert.True(row.ActsForUsers);
        Assert.Equal(MemoryApp.Owner, row.CreatedBy);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(owners)), row.SecretHash);
    }

    [Fact]
    public async Task FlaggedSecret_SupervisorRevoke_IsForbidden()
    {
        var owners = await App.IssueSecretAsync("seed", actsForUsers: true);

        var revoke = await App.SupervisorClient().DeleteAsync(Path);
        var mcp = await App.PostMcpAsync("tools/list", $"Bearer {owners}");

        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mcp.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.True((await db.AgentSecrets.SingleAsync()).ActsForUsers);
    }

    [Fact]
    public async Task FlaggedSecret_TheOwnerMayRotateAndRevoke()
    {
        var owners = await App.IssueSecretAsync("seed", actsForUsers: true);

        var rotate = await App.OwnerClient().PostAsJsonAsync(Path, new { actsForUsers = true });
        var withOld = await App.PostMcpAsync("tools/list", $"Bearer {owners}");
        var revoke = await App.OwnerClient().DeleteAsync(Path);

        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(0, await db.AgentSecrets.CountAsync());
    }

    [Fact]
    public async Task UnflaggedSecret_ASupervisorMayStillRotateAndRevoke()
    {
        var old = await App.IssueSecretAsync("seed");

        var rotate = await App.SupervisorClient().PostAsync(Path, null);
        var withOld = await App.PostMcpAsync("tools/list", $"Bearer {old}");
        var revoke = await App.SupervisorClient().DeleteAsync(Path);

        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
    }

    [Fact]
    public async Task Refusals_AreAuditedAtWarning_WithActorAgentAndReason()
    {
        var owners = await App.IssueSecretAsync("seed", actsForUsers: true);
        await App.SupervisorClient().PostAsJsonAsync("/api/memory/agents/other/secret", new { actsForUsers = true });
        await App.SupervisorClient().PostAsync(Path, null);
        await App.SupervisorClient().DeleteAsync(Path);
        await App.Client("olga", SkanyxxRoles.Employee).PostAsync(Path, null);
        await App.Client("olga", SkanyxxRoles.Employee).DeleteAsync(Path);

        var refusals = App.Logs.Split('\n').Where(l => l.StartsWith("Warning Agent memory secret") && l.Contains(" refused ")).ToList();

        Assert.Equal(5, refusals.Count);
        Assert.Contains($"issue refused for other by {MemoryApp.Supervisor} from 127.0.0.1; acts for users requested: True; reason: Only the owner may let an agent act for users.", refusals[0]);
        Assert.Contains($"issue refused for seed by {MemoryApp.Supervisor} from 127.0.0.1; acts for users requested: False; reason: Only the owner may rotate", refusals[1]);
        Assert.Contains($"revoke refused for seed by {MemoryApp.Supervisor} from 127.0.0.1; reason: Only the owner may revoke", refusals[2]);
        Assert.Contains("issue refused for seed by olga from 127.0.0.1; acts for users requested: False; reason: Only a supervisor", refusals[3]);
        Assert.Contains("revoke refused for seed by olga from 127.0.0.1; reason: Only a supervisor", refusals[4]);
        Assert.All(refusals, r => Assert.DoesNotContain("skx_mem_", r));
        Assert.DoesNotContain(owners, App.Logs);
    }

    [Fact]
    public async Task WithoutActsForUsers_UserHeaderIsIgnored_AndPersonalIsRefused()
    {
        await App.Client(Users.Ana).PutCardAsync($"personal:{Users.Ana}", "ana-refund");
        await App.SupervisorClient().PutCardAsync("company", "company-refund");
        var secret = await App.IssueSecretAsync("seed");
        await using var client = await App.McpWithSecretAsync(secret, userId: Users.Ana);

        var search = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" });
        var upsert = await client.CallToolAsync("memory_upsert", Upsert("personal"));

        Assert.Contains("company-refund", Text(search));
        Assert.DoesNotContain("ana-refund", Text(search));
        Assert.True(upsert.IsError);
        Assert.Contains("No user context", Text(upsert));
        Assert.Equal(2, await Postgres.CardCountAsync());
    }

    [Fact]
    public async Task WithActsForUsers_TheNamedUsersPersonalScopeIsUsed()
    {
        await App.Client(Users.Ana).PutCardAsync($"personal:{Users.Ana}", "ana-refund");
        var secret = await App.IssueSecretAsync("seed", actsForUsers: true);
        await using var client = await App.McpWithSecretAsync(secret, userId: Users.Ana);

        var search = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" });
        var upsert = await client.CallToolAsync("memory_upsert", Upsert("personal"));

        Assert.Contains("ana-refund", Text(search));
        Assert.NotEqual(true, upsert.IsError);
        Assert.Contains($"{Users.Ana} via seed", Text(upsert));
    }

    // Only a canonical (lowercase, hyphenated) GUID is a Skanyxx user id; anything else is no user, not a new scope.
    [Theory]
    [InlineData("ana")]
    [InlineData("admin@kagent.dev")]
    [InlineData("0A0A0A0A-0000-4000-8000-000000000001")]
    [InlineData("{0a0a0a0a-0000-4000-8000-000000000001}")]
    [InlineData("0a0a0a0a000040008000000000000001")]
    public async Task NonGuidUserId_IsTreatedAsNoUser(string userId)
    {
        var secret = await App.IssueSecretAsync("seed", actsForUsers: true);
        await using var client = await App.McpWithSecretAsync(secret, userId: userId);

        var upsert = await client.CallToolAsync("memory_upsert", Upsert("personal"));

        Assert.True(upsert.IsError);
        Assert.Contains("No user context", Text(upsert));
        Assert.Equal(0, await Postgres.CardCountAsync());
    }

    // What reaches the parser when a proxy passes padding through, or a request repeats the header (joined with ',').
    [Theory]
    [InlineData(" " + Users.Ana)]
    [InlineData(Users.Ana + " ")]
    [InlineData("\t" + Users.Ana)]
    [InlineData(Users.Ana, Users.Ana)]
    [InlineData(Users.Ana, Users.Bob)]
    public void PaddedOrRepeatedUserId_IsNoUser(params string[] values)
    {
        var context = ActingAgent(values);

        Assert.Null(Endpoints.MemoryHeaders.Agent(context).UserId);
    }

    [Fact]
    public void CanonicalUserId_IsTheUser() // the control for the theory above
    {
        Assert.Equal(Users.Ana, Endpoints.MemoryHeaders.Agent(ActingAgent(Users.Ana)).UserId);
    }

    private static Microsoft.AspNetCore.Http.DefaultHttpContext ActingAgent(params string[] userIds)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "seed"),
                new System.Security.Claims.Claim(Access.AgentSecretAuthentication.ActsForUsersClaim, "true")
            ], "test"))
        };
        context.Request.Headers[Endpoints.MemoryHeaders.UserId] = new Microsoft.Extensions.Primitives.StringValues(userIds);
        return context;
    }

    // The policy's own scheme replaces the signed-in user: the caller is the agent (+ the user it names), never the human.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignedInUserPlusSecret_IsTheAgent_WithTheUserOnlyWhenActingForUsers(bool actsForUsers)
    {
        var secret = await App.IssueSecretAsync("seed", actsForUsers);
        await using var client = await App.McpWithSecretAsync(secret, userId: Users.Ana, headers: new Dictionary<string, string>
        {
            [TestAuthHandler.UserHeader] = MemoryApp.Supervisor, [TestAuthHandler.RolesHeader] = SkanyxxRoles.Supervisor
        });

        var personal = await client.CallToolAsync("memory_upsert", Upsert("personal"));
        var company = await client.CallToolAsync("memory_upsert", Upsert("company"));

        Assert.True(company.IsError); // a supervisor could write company; the default agent grant cannot
        if (actsForUsers)
            Assert.Contains($"{Users.Ana} via seed", Text(personal));
        else
            Assert.Contains("No user context", Text(personal));
        Assert.DoesNotContain(MemoryApp.Supervisor, Text(personal));
    }

    [Fact]
    public async Task IssueRotateRevoke_AreAuditedAtWarning_WithoutTheSecret()
    {
        var first = await App.IssueSecretAsync("seed");
        var second = await App.IssueSecretAsync("seed", actsForUsers: true);
        await App.OwnerClient().DeleteAsync(Path);

        var warnings = App.Logs.Split('\n').Where(l => l.StartsWith("Warning Agent memory secret")).ToList();

        Assert.Equal(3, warnings.Count);
        Assert.Contains($"issued for seed by {MemoryApp.Supervisor} from 127.0.0.1; acts for users: False", warnings[0]);
        Assert.Contains($"issued for seed by {MemoryApp.Owner} from 127.0.0.1; acts for users: True", warnings[1]);
        Assert.Contains($"revoked for seed by {MemoryApp.Owner} from 127.0.0.1", warnings[2]);
        Assert.All(warnings, w => Assert.DoesNotContain("skx_mem_", w));
        Assert.DoesNotContain(first, App.Logs);
        Assert.DoesNotContain(second, App.Logs);
    }

    [Fact]
    public async Task Refusal_NeverEchoesThePresentedSecret()
    {
        var revoked = await App.IssueSecretAsync("seed");
        await App.SupervisorClient().DeleteAsync(Path);

        foreach (var presented in new[] { revoked, UnknownSecret, "not-a-secret-but-sensitive" })
        {
            var response = await App.PostMcpAsync("tools/list", $"Bearer {presented}");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain(presented, body);
            Assert.DoesNotContain(presented, response.Headers.WwwAuthenticate.ToString());
        }
    }

    [Fact]
    public void AgentCaller_WithoutAnAuthenticatedAgent_FailsLoudly()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers[Endpoints.MemoryHeaders.UserId] = Users.Ana;

        Assert.Throws<InvalidOperationException>(() => Endpoints.MemoryHeaders.Agent(context));
    }

    private static Dictionary<string, object?> Upsert(string scope) => new()
    {
        ["key"] = "refund-window", ["type"] = "decision", ["what"] = "We refund within 14 days",
        ["why"] = "Finance policy X", ["version"] = 0, ["scope"] = scope
    };
}
