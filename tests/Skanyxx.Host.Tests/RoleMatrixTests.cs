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
/// (401, 403, or the 404 of nothing there) unless <see cref="BelowSupervisor"/> lets that role write it. D394: every API
/// read the same way against <see cref="ReadsBelowSupervisor"/>, and a route with a parameter must be refused by its roles
/// (401/403): a made-up id is a 404 from any handler, which says nothing about a real one. Razor page form posts are not
/// API routes: the page's own roles and antiforgery guard them.
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
        ["PUT api/chat/sessions/{id}/tickets"] = (Everyone, "the caller's own session; anyone else's is 404 or 403 (OwnSession)"),
        ["DELETE api/chat/sessions/{id}"] = (Everyone, "the caller's own session; anyone else's is 404 or 403 (OwnSession)"),
        ["DELETE api/chat/files/{id}"] = (Everyone, "the caller's own file; anyone else's is 404 (ChatFiles keys by owner)"),

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
        ["DELETE api/investigate/workflows/{id}"] = (Builders, "its owner, the owner, or a supervisor of its owner's team; else 404/403 (WorkflowAccess)"),
        ["POST api/investigate/workflows/{id}/duplicate"] = (Builders, "a workflow the caller may see, copied as their own; else 404 (WorkflowAccess)"),
        ["POST api/investigate/runs/{id}/cancel"] = (Builders, "a run the caller may see: theirs, or their team's for a supervisor; else 404 (WorkflowAccess, D371)"),
        ["POST api/investigate/runs/{id}/rerun"] = (Builders, "a run the caller may see, started again as the caller; else 404 (WorkflowAccess, D371)"),
    };

    /// <summary>
    /// The API reads a role below supervisor may reach, and why. Reviewed line by line, like <see cref="BelowSupervisor"/>:
    /// a read belongs here only when what it returns is the caller's own, is filtered to what the caller may see by a rule
    /// that is not a role (ownership, sharing, scope membership, the starter's team), or is the catalog of the panels that
    /// role has (D394).
    /// </summary>
    private static readonly Dictionary<string, (string[] Roles, string Why)> ReadsBelowSupervisor = new()
    {
        // Anonymous by design: what the sign-in page needs before anyone is signed in.
        ["GET api/identity/status"] = (Everyone, "whether the install is set up and which sign-ins it offers; anonymous"),

        // The caller's own.
        ["GET api/identity/me"] = (Everyone, "the caller's own account"),
        ["GET api/me/preferences"] = (Everyone, "the caller's own preferences"),
        ["GET api/quotas/me"] = (Everyone, "the caller's own quota"),
        ["GET api/chat/limits"] = (Everyone, "the caller's own chat limits"),
        ["GET api/chat/conversations"] = (Everyone, "the caller's own conversations (kagent lists them as the caller)"),
        ["GET api/chat/conversations/{id}"] = (Everyone, "the caller's own conversation (kagent looks it up as the caller)"),
        ["GET api/chat/files/{id}"] = (Everyone, "the caller's own file; anyone else's is 404 (ChatFiles keys by owner)"),
        ["GET api/chat/cache"] = (Everyone, "counts of the caller's own cached answers; everyone's only for the owner (handler)"),

        // Chat is what everyone is here for: the agents a person can talk to, as the chat panel lists them.
        ["GET api/chat/agents"] = (Everyone, "the agents the caller can chat with"),
        ["GET api/chat/agents/{namespace}/{name}"] = (Everyone, "one agent the caller can chat with"),
        ["GET api/agents"] = (Everyone, "the agents catalog (endpoint roles name employee on purpose); no secrets, no tools' credentials"),
        ["GET api/agents/options"] = (Everyone, "the choices the agents catalog offers"),
        ["GET api/agents/setup"] = (Everyone, "whether agents are set up, for the empty state"),
        ["GET api/agents/{namespace}/{name}"] = (Everyone, "one agent of the catalog"),

        // Filtered by a rule that is not a role.
        ["GET api/chat/sessions"] = (Everyone, "only sessions the caller may read: their own and those shared with their teams (SessionAccess)"),
        ["GET api/chat/sessions/{id}"] = (Everyone, "only a session the caller may read; anyone else's is 404 (SessionAccess)"),
        ["GET api/chat/tickets/{key}"] = (Everyone, "only readable sessions; the ticket's runs only for supervisors (D394)"),
        ["GET api/analytics/usage"] = (Everyone, "the caller's own usage; a team or the company only where AnalyticsViewer reaches"),
        ["GET api/analytics/scopes"] = (Everyone, "the drill-down the caller may open (just \"Me\" for most people)"),
        ["GET api/memory/cards"] = (Everyone, "searches only the scopes AccessPolicy lets the caller read"),
        ["GET api/memory/cards/{scope}/{key}"] = (Everyone, "a card in a scope the caller may read; else 403/404 (AccessPolicy)"),
        ["GET api/memory/cards/{scope}/{key}/body"] = (Everyone, "a card in a scope the caller may read; else 403/404 (AccessPolicy)"),
        ["GET api/memory/cards/{scope}/{key}/history"] = (Everyone, "a card in a scope the caller may read; else 403/404 (AccessPolicy)"),
        ["GET api/memory/library"] = (Everyone, "the scopes AccessPolicy lets the caller read"),

        // Builders' panels (NavCatalog: Studio, Plugins, Investigate).
        ["GET api/studio"] = (Builders, "Studio is the builders' panel: open proposals are pull requests in skanyxx-agents"),
        ["GET api/studio/options"] = (Builders, "the choices the Studio form offers"),
        ["GET api/studio/templates"] = (Builders, "the Studio's starting templates"),
        ["GET api/studio/ci-template"] = (Builders, "the CI workflow a supervisor installs; holds no secret"),
        ["GET api/studio/changes"] = (Builders, "each change's pull request, checks, merge and deploy (D365)"),
        ["GET api/studio/agents/{name}/draft"] = (Builders, "an agent in main as a form, to propose a change (D2)"),
        ["GET api/studio/proposals/{number}"] = (Builders, "a pull request in skanyxx-agents"),
        ["GET api/plugins"] = (Builders, "Plugins is the builders' panel"),
        ["GET api/plugins/catalog"] = (Builders, "the plugin catalog"),
        ["GET api/investigate/options"] = (Builders, "Investigate is the builders' panel: the agents and teams a step may use"),
        ["GET api/investigate/workflows"] = (Builders, "only workflows the caller may see: their own and their teams' (WorkflowAccess)"),
        ["GET api/investigate/workflows/{id}"] = (Builders, "only a workflow the caller may see; else 404 (WorkflowAccess)"),
        ["GET api/investigate/runs"] = (Builders, "only runs the caller may see: theirs, or their team's for a supervisor (WorkflowAccess, D371)"),
        ["GET api/investigate/runs/{id}"] = (Builders, "only a run the caller may see; else 404 (WorkflowAccess, D371)"),
        ["GET api/investigate/runs/{id}/report.json"] = (Builders, "only a run the caller may see; else 404 (WorkflowAccess, D371)"),
        ["GET api/investigate/runs/{id}/report.pdf"] = (Builders, "only a run the caller may see; else 404 (WorkflowAccess, D371)"),
    };

    private static readonly HashSet<string> Writes = ["POST", "PUT", "PATCH", "DELETE"];
    private static readonly HashSet<string> Reads = ["GET"];

    [Fact]
    public async Task BelowSupervisor_EveryWriteOutsideTheAllowList_IsRefused() =>
        await AssertRefusedAsync("writes", host => Routes(host, Writes), BelowSupervisor);

    /// <summary>D394: the same for every API read, so a panel's data cannot be read around its page's roles.</summary>
    [Fact]
    public async Task BelowSupervisor_EveryReadOutsideTheAllowList_IsRefused() =>
        await AssertRefusedAsync("reads", host => [.. Routes(host, Reads).Where(r => r.Pattern.StartsWith("api/", StringComparison.Ordinal))], ReadsBelowSupervisor);

    [Fact]
    public void TheAllowLists_NameOnlyRoutesThatExist()
    {
        var writes = Routes(fixture.Host, Writes).Select(r => r.Key).ToHashSet();
        var reads = Routes(fixture.Host, Reads).Select(r => r.Key).ToHashSet();

        Assert.All(BelowSupervisor.Keys, k => Assert.Contains(k, writes));
        Assert.All(ReadsBelowSupervisor.Keys, k => Assert.Contains(k, reads));
    }

    /// <summary>
    /// Every route outside the allow-list is refused as an employee and as a builder. A route with a parameter must be
    /// refused by its roles (401 or 403), before the handler runs: the probe's made-up id would make any handler answer
    /// 404, so a 404 there proves nothing about who may act on a real one (D394).
    /// </summary>
    private async Task AssertRefusedAsync(string matrix, Func<HostApp, List<Route>> routes, Dictionary<string, (string[] Roles, string Why)> allowList)
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s => s["Skanyxx:RateLimit:PermitLimit"] = "100000");
        var owner = await host.OwnerAsync();
        var people = new Dictionary<string, HttpClient>
        {
            [SkanyxxRoles.Employee] = (await TestPeople.InviteAsync(host, owner, $"{matrix}-employee@skanyxx.example", SkanyxxRoles.Employee)).Api,
            [SkanyxxRoles.Builder] = (await TestPeople.InviteAsync(host, owner, $"{matrix}-builder@skanyxx.example", SkanyxxRoles.Builder)).Api
        };

        var problems = new List<string>();
        foreach (var route in routes(host))
            foreach (var (role, client) in people)
            {
                if (allowList.TryGetValue(route.Key, out var allowed) && allowed.Roles.Contains(role))
                    continue;
                using var response = await client.SendAsync(Probe(route), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
                var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    || (response.StatusCode == HttpStatusCode.NotFound && !Parameter().IsMatch(route.Pattern));
                if (!refused)
                    problems.Add($"{route.Key} as {role}: {(int)response.StatusCode}");
            }

        Assert.True(problems.Count == 0, "Reachable below supervisor:\n" + string.Join("\n", problems));
        // Nothing above signed them out: a 401 everywhere would pass the matrix without testing it.
        foreach (var client in people.Values)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    private sealed record Route(string Method, string Pattern, bool Multipart)
    {
        public string Key => $"{Method} {Pattern}";
    }

    private static List<Route> Routes(HostApp host, HashSet<string> methods) =>
        [.. host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Where(methods.Contains)
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
        else if (route.Method is not ("DELETE" or "GET"))
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
