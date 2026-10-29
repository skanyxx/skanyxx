using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D392, requirements §2 ("nobody who is not a supervisor can do harm"): an employee and a builder call every write route
/// this build maps — enumerated from the running host's endpoints, so a new one cannot be missed — and each is refused
/// (401, 403, or the 404 of nothing there) unless <see cref="BelowSupervisor"/> lets that role write it. Razor page form
/// posts are not API routes: the page's own roles and antiforgery guard them.
/// </summary>
[Collection(HostCollection.Name)]
public sealed partial class RoleMatrixTests(PostgresFixture fixture)
{
    private static readonly string[] Everyone = [SkanyxxRoles.Employee, SkanyxxRoles.Builder];
    private static readonly string[] Builders = [SkanyxxRoles.Builder];

    /// <summary>
    /// The write routes a role below supervisor may reach, and why. Reviewed line by line: a route belongs here only when
    /// what it writes is the caller's own, needs a one-time credential, or is refused further in by a rule that is not a
    /// role (ownership, scope membership, a pull request a supervisor merges).
    /// </summary>
    private static readonly Dictionary<string, (string[] Roles, string Why)> BelowSupervisor = new()
    {
        // Anonymous by design (README "Anonymous"): each needs a one-time token or a password, and the credential posts
        // have their own rate limit. A signed-in caller gains nothing from them.
        ["POST api/identity/bootstrap"] = (Everyone, "owner setup; needs the bootstrap token"),
        ["POST api/identity/unlock"] = (Everyone, "break-glass owner unlock; needs the bootstrap token"),
        ["POST api/identity/sign-in"] = (Everyone, "a password sign-in"),
        ["POST api/identity/refresh"] = (Everyone, "needs the caller's own refresh token"),
        ["POST api/identity/invites/lookup"] = (Everyone, "needs an invite token"),
        ["POST api/identity/invites/accept"] = (Everyone, "needs an invite token"),
        ["POST api/identity/password/forgot"] = (Everyone, "mails a reset link to the address given; answers the same either way"),
        ["POST api/identity/password/lookup"] = (Everyone, "needs a reset token"),
        ["POST api/identity/password/reset"] = (Everyone, "needs a reset token"),

        // The caller's own.
        ["POST api/identity/sign-out"] = (Everyone, "ends the caller's own sessions (never called here: it would sign the matrix out)"),
        ["PUT api/me/preferences"] = (Everyone, "the caller's own preferences"),
        ["DELETE api/me/preferences"] = (Everyone, "the caller's own preferences"),
        ["POST api/quotas/requests"] = (Everyone, "asks a supervisor for more of the caller's own quota"),

        // Chat is what everyone is here for (spend bounded by quotas); every item is the caller's own in kagent.
        ["POST api/chat"] = (Everyone, "a chat turn as the caller"),
        ["POST api/chat/stream"] = (Everyone, "a chat turn as the caller"),
        ["POST api/chat/files"] = (Everyone, "a file for the caller's own conversation"),
        ["PATCH api/chat/conversations/{id}"] = (Everyone, "the caller's own conversation (kagent looks it up as the caller)"),
        ["DELETE api/chat/conversations/{id}"] = (Everyone, "the caller's own conversation (kagent looks it up as the caller)"),
        ["DELETE api/chat/cache"] = (Everyone, "the caller's own cached answers; all of them only for the owner (handler)"),
        ["PUT api/chat/sessions/{id}/scope"] = (Everyone, "the caller's own session; the target scope is checked like a memory write"),

        // Memory: what a person may write is decided per scope (AccessPolicy): personal and member teams, company only a supervisor.
        ["PUT api/memory/cards/{scope}/{key}"] = (Everyone, "scope rules: personal, member teams and departments; company needs a supervisor"),
        ["POST api/memory/cards/{scope}/{key}/lift"] = (Everyone, "scope rules on both ends, as a write into the target"),
        ["POST api/memory/cards/{scope}/{key}/rename"] = (Everyone, "scope rules, as a write"),
        ["POST api/memory/cards/{scope}/{key}/status"] = (Everyone, "scope rules, as a write"),

        // D312: proposing is a pull request in skanyxx-agents; nothing changes until a supervisor merges it.
        ["POST api/agents/proposals"] = (Everyone, "opens a pull request a supervisor merges"),

        // Builders draft (D022): previews run without MCP tools (D392), plugins become pull requests.
        ["POST api/studio/manifest"] = (Builders, "renders a draft as YAML; writes nothing"),
        ["POST api/studio/factory"] = (Builders, "drafts an agent with a model turn; writes nothing to kagent"),
        ["POST api/studio/proposals"] = (Builders, "opens a pull request a supervisor merges"),
        ["POST api/studio/proposals/{number}/preview"] = (Builders, "a throwaway preview without MCP tools (D392)"),
        ["POST api/studio/proposals/{number}/chat"] = (Builders, "a turn with that preview, without MCP tools (D392)"),
        ["POST api/studio/proposals/{number}/close"] = (Builders, "closes a pull request; the handler allows only its author or a supervisor"),
        ["POST api/plugins/inspect"] = (Builders, "reads a plugin from the catalog; writes nothing"),
        ["POST api/plugins/install"] = (Builders, "opens a pull request a supervisor merges"),
        ["POST api/plugins/{name}/uninstall"] = (Builders, "opens a pull request a supervisor merges"),
        ["POST api/investigate/runs"] = (Builders, "Investigate is the builders' panel; steps count against the starter's quota (D385)"),
        ["POST api/investigate/workflows"] = (Builders, "Investigate is the builders' panel; a workflow is its author's"),
        ["PUT api/investigate/workflows/{id}"] = (Builders, "Investigate is the builders' panel; a workflow is its author's"),
    };

    private static readonly HashSet<string> Writes = ["POST", "PUT", "PATCH", "DELETE"];

    [Fact]
    public async Task BelowSupervisor_EveryWriteOutsideTheAllowList_IsRefused()
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s => s["Skanyxx:RateLimit:PermitLimit"] = "100000");
        var owner = await host.OwnerAsync();
        var people = new Dictionary<string, HttpClient>
        {
            [SkanyxxRoles.Employee] = (await TestPeople.InviteAsync(host, owner, "matrix-employee@skanyxx.example", SkanyxxRoles.Employee)).Api,
            [SkanyxxRoles.Builder] = (await TestPeople.InviteAsync(host, owner, "matrix-builder@skanyxx.example", SkanyxxRoles.Builder)).Api
        };

        var problems = new List<string>();
        foreach (var route in WriteRoutes(host))
            foreach (var (role, client) in people)
            {
                if (BelowSupervisor.TryGetValue(route.Key, out var allowed) && allowed.Roles.Contains(role))
                    continue;
                var status = (await client.SendAsync(Probe(route), TestContext.Current.CancellationToken)).StatusCode;
                if (status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
                    problems.Add($"{route.Key} as {role}: {(int)status}");
            }

        Assert.True(problems.Count == 0, "Reachable below supervisor:\n" + string.Join("\n", problems));
        // Nothing above signed them out: a 401 everywhere would pass the matrix without testing it.
        foreach (var client in people.Values)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public void TheAllowLists_NameOnlyRoutesThatExist()
    {
        var routes = WriteRoutes(fixture.Host).Select(r => r.Key).ToHashSet();

        Assert.All(BelowSupervisor.Keys, k => Assert.Contains(k, routes));
    }

    private sealed record Route(string Method, string Pattern, bool Multipart)
    {
        public string Key => $"{Method} {Pattern}";
    }

    private static List<Route> WriteRoutes(HostApp host) =>
        [.. host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Where(Writes.Contains)
                .Select(m => new Route(m, e.RoutePattern.RawText!.TrimStart('/'),
                    e.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true)))
            .Distinct()
            .OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal)];

    /// <summary>A request the route's content type admits, so a refusal is the rules' and not routing's (415).</summary>
    private static HttpRequestMessage Probe(Route route)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), "/" + Fill(route.Pattern));
        if (route.Multipart)
        {
            var file = new ByteArrayContent([0x50, 0x4B, 0x05, 0x06]);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            request.Content = new MultipartFormDataContent { { file, "file", "probe.zip" } };
        }
        else if (route.Method != "DELETE")
            request.Content = JsonContent.Create(new { });
        return request;
    }

    /// <summary>
    /// A value each parameter binds: the company scope for a memory scope, else a GUID, which is also a valid slug, name
    /// and key, so a refusal comes from the rules and not from binding.
    /// </summary>
    private static string Fill(string pattern) =>
        Parameter().Replace(pattern, m => m.Groups["name"].Value == "scope" ? "company" : "3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    [GeneratedRegex(@"\{\*{0,2}(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\??\}")]
    private static partial Regex Parameter();
}
