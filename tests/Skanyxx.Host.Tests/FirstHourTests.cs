using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Slice 1 "first hour" (todo.md): Setup → the owner's model step (the default kagent ModelConfig, D3, D323) → Chat with
/// merged agents only (D4), as the signed-in person (D5). Against <see cref="FakeKAgent"/>.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class FirstHourTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string EmployeeEmail = "eli@skanyxx.example";
    private const string EmployeePassword = "a long employee passphrase";
    private const int ControlTimeoutSeconds = 2;
    private const int ChatTimeoutSeconds = 4;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private Browser _owner = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["KAgent:ControlTimeoutSeconds"] = ControlTimeoutSeconds.ToString();
            s["KAgent:ChatTimeoutSeconds"] = ChatTimeoutSeconds.ToString();
        });
        _owner = new Browser(_host);
        var setup = await _owner.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });
        Assert.Equal("/Model", setup.Headers.Location?.OriginalString);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    [Fact]
    public async Task Home_IsTheModelStep_UntilTheDefaultModelExists_ThenChat()
    {
        Assert.Equal("/Model", (await _owner.GetAsync("/")).Headers.Location?.OriginalString);
        var page = await (await _owner.GetAsync("/Model")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("id=\"model-form\"", page);
        var owner = await _host.OwnerAsync();

        var saved = await owner.PostAsJsonAsync("/api/models", new
        {
            @namespace = "kagent", name = "default-model-config", provider = "Ollama", model = "qwen3-coder:30b",
            @params = new { host = "http://host.docker.internal:11434" }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        Assert.True((await saved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("isDefault").GetBoolean());
        // D350: once the model works, the owner lands on the Dashboard (Settings → General → landing page per role).
        Assert.Equal("/Dashboard", (await _owner.GetAsync("/")).Headers.Location?.OriginalString);
        var create = Assert.Single(_kagent.CallsTo("POST", "/api/modelconfigs"));
        Assert.Equal("kagent/default-model-config", create.Body!["ref"]!.GetValue<string>());
        Assert.Null(create.Body["apiKey"]);
        Assert.Equal("""{"provider":"Ollama","model":"qwen3-coder:30b","ollama":{"host":"http://host.docker.internal:11434"}}""",
            create.Body["spec"]!.ToJsonString());
        Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync("/Chat")).StatusCode);
    }

    [Fact]
    public async Task Home_StaysOnTheModelStep_WhileKAgentHasNotAcceptedIt()
    {
        _kagent.ModelSpec = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3" };
        _kagent.ModelAccepted = false;
        var owner = await _host.OwnerAsync();

        Assert.Equal("/Model", (await _owner.GetAsync("/")).Headers.Location?.OriginalString);
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/models", TestContext.Current.CancellationToken);
        var model = list.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("isDefault").GetBoolean());
        Assert.False(model.GetProperty("accepted").GetBoolean());
        Assert.Equal("secret not found", model.GetProperty("statusMessage").GetString());
    }

    [Fact]
    public async Task ModelStep_IsTheOwnersOnly()
    {
        var (employee, employeeBrowser) = await EmployeeAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/models", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().GetAsync("/api/models", TestContext.Current.CancellationToken)).StatusCode);
        var page = await employeeBrowser.GetAsync("/Model");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/AccessDenied", page.Headers.Location?.OriginalString);
        // Everyone else's home is Chat, without asking kagent about the model.
        Assert.Equal("/Chat", (await employeeBrowser.GetAsync("/")).Headers.Location?.OriginalString);
        Assert.DoesNotContain(_kagent.Calls, c => c.Path.StartsWith("/api/modelconfigs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelStep_WhenKAgentIsDown_Is502()
    {
        var owner = await fixture.Host.OwnerAsync();

        var response = await owner.GetAsync("/api/models", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("kagent did not answer", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChatLists_EveryAgentButSkanyxxsOwn()
    {
        var (employee, _) = await EmployeeAsync();

        var agents = await employee.GetFromJsonAsync<JsonElement>("/api/chat/agents", cancellationToken: TestContext.Current.CancellationToken);

        // D557: e2e-hello carries no label at all; ticket-planner is a Tickets stage, never a chat partner.
        Assert.Equal(["kagent/e2e-hello", "kagent/seed", "team-a/seed"],
            agents.EnumerateArray().Select(a => $"{a.GetProperty("namespace").GetString()}/{a.GetProperty("name").GetString()}"));
    }

    [Fact]
    public async Task Chat_TalksToTheSeed_AsTheSignedInPerson_AndContinuesTheConversation()
    {
        var owner = await _host.OwnerAsync();
        var me = (await owner.GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;

        // A user id in the body is not a field: the person is the signed-in one.
        var first = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "What is our refund window?", userId = "someone-else" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var answer = await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var conversation = answer.GetProperty("conversationId").GetString()!;
        Assert.Equal(_kagent.Answer, answer.GetProperty("message").GetProperty("content").GetString());

        var second = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "And for gift cards?", conversationId = conversation }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var session = Assert.Single(_kagent.CallsTo("POST", "/api/sessions"));
        Assert.Equal(me, session.QueryUser);
        Assert.Equal("kagent/seed", session.Body!["agent_ref"]!.GetValue<string>());
        var turns = _kagent.CallsTo("POST", "/api/a2a/kagent/seed/").ToList();
        Assert.Equal(2, turns.Count);
        Assert.All(turns, t =>
        {
            Assert.Equal(me, t.QueryUser);
            Assert.Equal(me, t.HeaderUser);
            Assert.Equal(conversation, t.Body!["params"]!["message"]!["contextId"]!.GetValue<string>());
        });
        Assert.Equal(me, Assert.Single(_kagent.CallsTo("GET", "/api/sessions/")).QueryUser);
    }

    [Fact]
    public async Task Chat_RefusesAnAgentThatIsNotMerged()
    {
        var owner = await _host.OwnerAsync();

        var response = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "ticket-planner", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_kagent.CallsTo("POST", "/api/sessions"));
        Assert.Empty(_kagent.CallsTo("POST", "/api/a2a/"));
    }

    [Fact]
    public async Task Chat_RefusesSomeoneElsesConversation()
    {
        var owner = await _host.OwnerAsync();
        var (employee, _) = await EmployeeAsync();
        var ownerAnswer = await (await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "private question" }, cancellationToken: TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);

        var (response, log) = await CapturingConsoleAsync(() => employee.PostAsJsonAsync("/api/chat",
            new { agentNamespace = "kagent", agentName = "seed", message = "what did they ask?", conversationId = ownerAnswer.GetProperty("conversationId").GetString() }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/"));
        // kagent's 404 for another person's session is routine: a Warning, not an Error.
        Assert.Contains("answered 404", log);
        Assert.DoesNotContain("\"@l\":\"Error\"", log);
    }

    [Fact]
    public async Task Chat_RefusesAConversationWithAnotherMergedAgent_OfTheSameName()
    {
        var owner = await _host.OwnerAsync();
        var first = await (await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);

        var other = await owner.PostAsJsonAsync("/api/chat",
            new { agentNamespace = "team-a", agentName = "seed", message = "continue", conversationId = first.GetProperty("conversationId").GetString() }, cancellationToken: TestContext.Current.CancellationToken);
        var fresh = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "team-a", agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/kagent/seed/"));
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/team-a/seed/"));
        Assert.Equal(["kagent/seed", "team-a/seed"],
            _kagent.CallsTo("POST", "/api/sessions").Select(c => c.Body!["agent_ref"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Chat_NeedsTheAgentsNamespace()
    {
        var owner = await _host.OwnerAsync();

        var response = await owner.PostAsJsonAsync("/api/chat", new { agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_kagent.Calls);
    }

    [Fact]
    public async Task Chat_NeedsASignedInPerson()
    {
        var response = await _host.Client().PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_kagent.Calls);
    }

    [Fact]
    public async Task ASlowKAgent_FailsControlCallsFast_AndTheHomePageDoesNotHang()
    {
        _kagent.AgentsDelay = _kagent.ModelDelay = TimeSpan.FromSeconds(ControlTimeoutSeconds + 3);
        var owner = await _host.OwnerAsync();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var agents = await owner.GetAsync("/api/chat/agents", TestContext.Current.CancellationToken);
        var home = await _owner.GetAsync("/");

        Assert.Equal(HttpStatusCode.BadGateway, agents.StatusCode);
        Assert.Equal("/Model", home.Headers.Location?.OriginalString);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2 * ControlTimeoutSeconds + 2), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task AChatTurn_GetsTheLongTimeout_AndOneThatTimesOut_LeavesNoSession()
    {
        var owner = await _host.OwnerAsync();

        // Longer than a control call may take, shorter than a turn may.
        _kagent.A2ADelay = TimeSpan.FromSeconds(ControlTimeoutSeconds + 1);
        var slow = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, slow.StatusCode);

        _kagent.A2ADelay = TimeSpan.FromSeconds(ChatTimeoutSeconds + 2);
        var tooSlow = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi again" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, tooSlow.StatusCode);
        Assert.Contains("did not answer in time", await tooSlow.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // The first conversation stays; the one created for the failed turn is deleted (as the same person).
        Assert.Single(_kagent.SessionIds);
        var delete = Assert.Single(_kagent.CallsTo("DELETE", "/api/sessions/"));
        Assert.Equal(_kagent.CallsTo("POST", "/api/sessions").Last().QueryUser, delete.QueryUser);
    }

    [Fact]
    public async Task KAgentFailing_IsAGeneric502_WithoutKAgentsText_AndLeavesNoSession()
    {
        var (employee, _) = await EmployeeAsync();
        _kagent.A2AStatus = 500;

        var response = await employee.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("cannot answer right now", body);
        Assert.DoesNotContain("SECRET-INTERNAL", body);
        Assert.DoesNotContain("HTTP 500", body);
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/")); // never retried: a turn is not idempotent
        Assert.Empty(_kagent.SessionIds);
    }

    [Theory]
    [InlineData(500, null)]
    [InlineData(null, """{"data": 5}""")]
    [InlineData(null, "not json")]
    public async Task AgentsListFailing_Is502_NotNoSuchAgent(int? status, string? body)
    {
        var owner = await _host.OwnerAsync();
        _kagent.AgentsStatus = status;
        _kagent.AgentsBody = body;

        var list = await owner.GetAsync("/api/chat/agents", TestContext.Current.CancellationToken);
        var chat = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, list.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, chat.StatusCode);
        Assert.DoesNotContain("SECRET-INTERNAL", await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","result":{"kind":5}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{"kind":"message","parts":[{"kind":"text","text":"x","metadata":{"adk_thought":"yes"}}]}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":[1,2]}""")]
    [InlineData("""[]""")]
    public async Task AnOddlyShapedAnswer_Is502_Not500(string answer)
    {
        var owner = await _host.OwnerAsync();
        _kagent.A2ABody = answer;

        var response = await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task ChatAndModel_AreOriginGuarded()
    {
        var owner = await _host.OwnerAsync();
        HttpRequestMessage Foreign(HttpMethod method, string path, object? body = null)
        {
            var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            request.Headers.Add("Origin", "https://evil.example");
            return request;
        }

        var chat = await owner.SendAsync(Foreign(HttpMethod.Post, "/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }), TestContext.Current.CancellationToken);
        var agents = await owner.SendAsync(Foreign(HttpMethod.Get, "/api/chat/agents"), TestContext.Current.CancellationToken);
        var model = await owner.SendAsync(Foreign(HttpMethod.Get, "/api/models"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, chat.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, agents.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, model.StatusCode);
        Assert.Empty(_kagent.Calls);
    }

    /// <summary>D108: a Host started without the script (no AllowedOrigins) still chats from where people reach it.</summary>
    [Fact]
    public async Task ThePublicBaseUrlsOrigin_MayChat_WithoutAllowedOrigins()
    {
        var database = await fixture.NewDatabaseAsync();
        var (host, log) = await CapturingConsoleAsync(() => HostApp.StartAsync(database, s =>
        {
            UseTheFakeKAgent(s);
            s.Remove("Skanyxx:AllowedOrigins:0");
            s["Identity:PublicBaseUrl"] = "https://Chat.Skanyxx.example/skanyxx/";
        }));
        await using var _ = host;
        var owner = await host.OwnerAsync();

        var own = await owner.SendAsync(ChatFrom("https://chat.skanyxx.example"), TestContext.Current.CancellationToken);
        var foreign = await owner.SendAsync(ChatFrom("https://evil.example"), TestContext.Current.CancellationToken);
        var otherPort = await owner.SendAsync(ChatFrom("https://chat.skanyxx.example:8443"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherPort.StatusCode);
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/"));
        Assert.DoesNotContain("Neither Skanyxx:AllowedOrigins nor Identity:PublicBaseUrl", log);
    }

    [Fact]
    public async Task WithoutPublicBaseUrlOrAllowedOrigins_EvenTheHostsOwnOrigin_MayNotChat_AndStartupWarns()
    {
        var database = await fixture.NewDatabaseAsync();
        var (host, log) = await CapturingConsoleAsync(() => HostApp.StartAsync(database, s =>
        {
            UseTheFakeKAgent(s);
            s.Remove("Skanyxx:AllowedOrigins:0");
            s.Remove("Identity:PublicBaseUrl");
        }));
        await using var _ = host;
        var owner = await host.OwnerAsync();

        var self = await owner.SendAsync(ChatFrom(host.BaseAddress.GetLeftPart(UriPartial.Authority)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Empty(_kagent.CallsTo("POST", "/api/a2a/"));
        Assert.Contains("Neither Skanyxx:AllowedOrigins nor Identity:PublicBaseUrl", log);
    }

    [Fact]
    public async Task AFirstTurnTheCallerAbandons_LeavesNoSession()
    {
        var owner = await _host.OwnerAsync();
        _kagent.A2ADelay = TimeSpan.FromSeconds(ChatTimeoutSeconds - 1);
        using var leave = new CancellationTokenSource();

        var turn = owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, leave.Token);
        Assert.True(await UntilAsync(() => _kagent.CallsTo("POST", "/api/a2a/").Any()), "the turn never reached kagent");
        leave.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
        Assert.True(await UntilAsync(() => _kagent.SessionIds.Count == 0), "the abandoned turn's session was not deleted");
        var delete = Assert.Single(_kagent.CallsTo("DELETE", "/api/sessions/"));
        Assert.Equal(Assert.Single(_kagent.CallsTo("POST", "/api/sessions")).QueryUser, delete.QueryUser);
    }

    private void UseTheFakeKAgent(Dictionary<string, string> settings)
    {
        settings["KAgent:BaseUrl"] = "127.0.0.1";
        settings["KAgent:Port"] = _kagent.Port.ToString();
    }

    private static HttpRequestMessage ChatFrom(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { agentNamespace = "kagent", agentName = "seed", message = "hi" })
        };
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static async Task<bool> UntilAsync(Func<bool> condition)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(2 * ChatTimeoutSeconds); DateTime.UtcNow < deadline; await Task.Delay(50))
        {
            if (condition())
                return true;
        }
        return condition();
    }

    /// <summary>Debug is the owner's diagnostics: modules and every health check, kagent's included; /health leaves kagent out.</summary>
    [Fact]
    public async Task Debug_IsTheOwnersDiagnostics_AndHealthLeavesKagentOut()
    {
        var (_, employeeBrowser) = await EmployeeAsync();

        var page = await (await _owner.GetAsync("/Debug")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var denied = await employeeBrowser.GetAsync("/Debug");
        var employeeNav = await (await employeeBrowser.GetAsync("/Chat")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var ownerNav = await (await _owner.GetAsync("/Chat")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var health = await (await _host.Client().GetAsync("/health", TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Contains("<td>kagent</td>", page);
        Assert.Contains("<td><span class=\"badge badge-success\"><span class=\"badge-dot\" aria-hidden=\"true\"></span>Healthy</span></td>", page);
        Assert.Contains("<code>memory</code>", page);
        Assert.Contains("<code>runtime</code>", page);
        Assert.Contains(_kagent.Calls, c => c.Method == "GET" && c.Path == "/health");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/AccessDenied", denied.Headers.Location?.OriginalString);
        Assert.DoesNotContain("href=\"/Debug\"", employeeNav);
        Assert.Contains("href=\"/Debug\"", ownerNav);
        Assert.False(health.GetProperty("entries").TryGetProperty("kagent", out _));
    }

    // A signed-in person on a page their role lacks was sent to /Login, which sends a signed-in visitor back to the
    // return URL: an endless redirect in the browser. They land on a 403 page instead.
    [Theory]
    [InlineData("/Studio")]
    [InlineData("/Sandboxes")]
    [InlineData("/People")]
    public async Task Employee_OnAPageTheirRoleLacks_GetsA403Page_NotALoginLoop(string path)
    {
        var (_, employeeBrowser) = await EmployeeAsync();

        var denied = await employeeBrowser.GetAsync(path);
        var landing = await employeeBrowser.GetAsync(denied.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/AccessDenied", new Uri(_host.BaseAddress, denied.Headers.Location!).AbsolutePath);
        Assert.Equal(HttpStatusCode.Forbidden, landing.StatusCode);
        Assert.Contains("Not allowed", await landing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<(T Result, string Console)> CapturingConsoleAsync<T>(Func<Task<T>> action)
    {
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            return (await action(), console.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private async Task<(HttpClient Api, Browser Browser)> EmployeeAsync()
    {
        var owner = await _host.OwnerAsync();
        var invite = await owner.PostAsJsonAsync("/api/identity/invites", new { email = EmployeeEmail, roles = new[] { "employee" } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;

        var browser = new Browser(_host);
        var accepted = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/identity/invites/accept")
        {
            Content = JsonContent.Create(new { token, password = EmployeePassword, useCookie = true })
        });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = EmployeeEmail, password = EmployeePassword });
        signIn.EnsureSuccessStatusCode();
        var bearer = (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString()!;
        return (_host.Client(bearer), browser);
    }
}
