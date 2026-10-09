using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Slice 1 "first hour" (todo.md): Setup → the owner's model step (kagent ModelConfig over kagent's API, D3) → Chat with
/// merged agents only (D4), as the signed-in person (D5). Against <see cref="FakeKAgent"/>.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class FirstHourTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string EmployeeEmail = "eli@skanyxx.example";
    private const string EmployeePassword = "a long employee passphrase";
    private const string ApiKey = "sk-test-0123456789abcdefSECRET";
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
    public async Task Home_IsTheModelStep_UntilTheModelIsConfigured_ThenChat()
    {
        Assert.Equal("/Model", (await _owner.GetAsync("/")).Headers.Location?.OriginalString);

        var saved = await _owner.SubmitAsync("/Model", new()
        {
            ["Provider"] = "Ollama", ["ModelId"] = "qwen3-coder:30b", ["BaseUrl"] = "http://host.docker.internal:11434", ["ApiKey"] = ""
        });

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal("/Chat", saved.Headers.Location?.OriginalString);
        Assert.Equal("/Chat", (await _owner.GetAsync("/")).Headers.Location?.OriginalString);
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

        Assert.Equal("/Model", (await _owner.GetAsync("/")).Headers.Location?.OriginalString);
        var page = await (await _owner.GetAsync("/Model")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("has not accepted it (secret not found)", page);
    }

    [Fact]
    public async Task PastedKey_GoesInlineToKAgent_AndIsNeverReturned_OrLogged()
    {
        var owner = await _host.OwnerAsync();
        var console = new StringWriter();
        var original = Console.Out;
        HttpResponseMessage response;
        Console.SetOut(console);
        try
        {
            response = await owner.PutAsJsonAsync("/api/model", new { provider = "OpenAI", model = "gpt-4.1", apiKey = ApiKey }, cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(original);
        }
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = JsonDocument.Parse(body).RootElement;
        Assert.True(settings.GetProperty("hasApiKey").GetBoolean());
        Assert.True(settings.GetProperty("configured").GetBoolean());
        Assert.DoesNotContain(ApiKey, body);
        Assert.DoesNotContain(ApiKey, console.ToString());
        Assert.Contains("Model settings", console.ToString());
        var create = Assert.Single(_kagent.CallsTo("POST", "/api/modelconfigs"));
        Assert.Equal(ApiKey, create.Body!["apiKey"]!.GetValue<string>());
        Assert.Null(create.Body["spec"]!["apiKeySecret"]);
        Assert.Equal(ApiKey, _kagent.Secret["OPENAI_API_KEY"]);
    }

    [Fact]
    public async Task SameProvider_WithoutAKey_KeepsTheKeyReference_AndTheOtherSettings()
    {
        _kagent.ModelSpec = new JsonObject
        {
            ["provider"] = "Ollama", ["model"] = "llama3",
            ["ollama"] = new JsonObject { ["host"] = "http://old:11434", ["options"] = new JsonObject { ["num_ctx"] = "32768" } }
        };
        var owner = await _host.OwnerAsync();

        var ollama = await owner.PutAsJsonAsync("/api/model", new { provider = "Ollama", model = "qwen3-coder:30b", baseUrl = "http://new:11434" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ollama.StatusCode);
        Assert.Equal("""{"provider":"Ollama","model":"qwen3-coder:30b","ollama":{"host":"http://new:11434","options":{"num_ctx":"32768"}}}""",
            _kagent.ModelSpec.ToJsonString());

        _kagent.ModelSpec = new JsonObject
        {
            ["provider"] = "OpenAI", ["model"] = "gpt-4o", ["apiKeySecret"] = "default-model-config", ["apiKeySecretKey"] = "OPENAI_API_KEY",
            ["openAI"] = new JsonObject { ["temperature"] = "0.2" }
        };
        var openAi = await owner.PutAsJsonAsync("/api/model", new { provider = "OpenAI", model = "gpt-4.1" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, openAi.StatusCode);
        var put = _kagent.CallsTo("PUT", FakeKAgent.ModelConfigPath).Last();
        Assert.Null(put.Body!["apiKey"]);
        Assert.Equal("""{"provider":"OpenAI","model":"gpt-4.1","apiKeySecret":"default-model-config","apiKeySecretKey":"OPENAI_API_KEY","openAI":{"temperature":"0.2"}}""",
            put.Body["spec"]!.ToJsonString());
    }

    [Fact]
    public async Task ProviderChange_StartsClean_AndAKeyedProviderNeedsAKey()
    {
        _kagent.ModelSpec = new JsonObject
        {
            ["provider"] = "Ollama", ["model"] = "llama3", ["ollama"] = new JsonObject { ["host"] = "http://old:11434" }
        };
        var owner = await _host.OwnerAsync();

        var noKey = await owner.PutAsJsonAsync("/api/model", new { provider = "Anthropic", model = "claude-sonnet-4-5" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, noKey.StatusCode);
        Assert.Contains("Paste the Anthropic API key", await noKey.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_kagent.CallsTo("PUT", FakeKAgent.ModelConfigPath));

        var withKey = await owner.PutAsJsonAsync("/api/model", new { provider = "Anthropic", model = "claude-sonnet-4-5", apiKey = ApiKey }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
        var put = Assert.Single(_kagent.CallsTo("PUT", FakeKAgent.ModelConfigPath));
        Assert.Equal("""{"provider":"Anthropic","model":"claude-sonnet-4-5"}""", put.Body!["spec"]!.ToJsonString());
        Assert.Equal(ApiKey, _kagent.Secret["ANTHROPIC_API_KEY"]);
    }

    [Theory]
    [InlineData("Ollama", "llama3", "sk-x", null)] // Ollama takes no key
    [InlineData("Bedrock", "anthropic.claude", null, null)] // not offered
    [InlineData("OpenAI", "gpt 4", "sk-x", null)] // not a model id
    [InlineData("OpenAI", "gpt-4.1", "sk-LEAK x", null)] // a key with a space
    [InlineData("Ollama", "llama3", null, null)] // Ollama needs its host
    [InlineData("Ollama", "llama3", null, "http://ollama:11434/?x=1")] // a query
    [InlineData("Ollama", "llama3", null, "file:///etc/passwd")]
    [InlineData("Ollama", "llama3", null, "http://user:pw@ollama:11434")]
    public async Task InvalidModelSettings_Are400_AndNeverReachKAgent(string provider, string model, string? apiKey, string? baseUrl)
    {
        var owner = await _host.OwnerAsync();

        var response = await owner.PutAsJsonAsync("/api/model", new { provider, model, apiKey, baseUrl }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (apiKey is not null)
            Assert.DoesNotContain(apiKey, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_kagent.Calls, c => c.Path.StartsWith("/api/modelconfigs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelStep_IsTheOwnersOnly()
    {
        var (employee, employeeBrowser) = await EmployeeAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/model", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync("/api/model", new { provider = "Ollama", model = "llama3" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().GetAsync("/api/model", TestContext.Current.CancellationToken)).StatusCode);
        var page = await employeeBrowser.GetAsync("/Model");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Login", page.Headers.Location?.OriginalString);
        // Everyone else's home is Chat, without asking kagent about the model.
        Assert.Equal("/Chat", (await employeeBrowser.GetAsync("/")).Headers.Location?.OriginalString);
        Assert.DoesNotContain(_kagent.Calls, c => c.Path.StartsWith("/api/modelconfigs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelStep_WhenKAgentIsDown_Is502()
    {
        var owner = await fixture.Host.OwnerAsync();

        var response = await owner.GetAsync("/api/model", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("kagent is not reachable", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChatLists_MergedAgentsOnly()
    {
        var (employee, _) = await EmployeeAsync();

        var agents = await employee.GetFromJsonAsync<JsonElement>("/api/chat/agents", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["kagent/seed", "team-a/seed"],
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

    /// <summary>I2 (D124): the sessions API shows a person only their own kagent sessions; the owner reads anyone's by name.</summary>
    [Fact]
    public async Task Sessions_AreReadOnlyByTheirOwner_OrByTheOwner()
    {
        var owner = await _host.OwnerAsync();
        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
        var (employee, _) = await EmployeeAsync();
        var employeeId = (await employee.GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
        string Conversation(JsonElement answer) => answer.GetProperty("conversationId").GetString()!;
        var ownerChat = Conversation(await (await owner.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "private question" }, cancellationToken: TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken));
        var employeeChat = Conversation(await (await employee.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "my question" }, cancellationToken: TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken));

        var listed = await employee.GetFromJsonAsync<JsonElement>("/api/sessions", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([employeeChat], listed.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync($"/api/sessions/{employeeChat}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/sessions/{ownerChat}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/sessions/{ownerChat}/events", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/sessions/{ownerChat}/messages", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync($"/api/sessions?user={ownerId}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync($"/api/sessions/{ownerChat}?user={ownerId}", TestContext.Current.CancellationToken)).StatusCode);

        var asOwner = await owner.GetFromJsonAsync<JsonElement>($"/api/sessions?user={employeeId}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([employeeChat], asOwner.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/sessions/{employeeChat}?user={employeeId}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/sessions/{ownerChat}?user={ownerId}", TestContext.Current.CancellationToken)).StatusCode);
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
    public async Task NewBaseUrl_ForTheStoredKey_NeedsTheKeyAgain()
    {
        _kagent.ModelSpec = new JsonObject
        {
            ["provider"] = "OpenAI", ["model"] = "gpt-4o", ["apiKeySecret"] = "default-model-config", ["apiKeySecretKey"] = "OPENAI_API_KEY",
            ["openAI"] = new JsonObject { ["baseUrl"] = "https://api.openai.com/v1" }
        };
        var owner = await _host.OwnerAsync();

        var moved = await owner.PutAsJsonAsync("/api/model", new { provider = "OpenAI", model = "gpt-4.1", baseUrl = "https://attacker.example/v1" }, cancellationToken: TestContext.Current.CancellationToken);
        var dropped = await owner.PutAsJsonAsync("/api/model", new { provider = "OpenAI", model = "gpt-4.1" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Contains("Paste the OpenAI API key again", await moved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, dropped.StatusCode);
        Assert.Empty(_kagent.CallsTo("PUT", FakeKAgent.ModelConfigPath));

        var same = await owner.PutAsJsonAsync("/api/model", new { provider = "OpenAI", model = "gpt-4.1", baseUrl = "https://api.openai.com/v1" }, cancellationToken: TestContext.Current.CancellationToken);
        var movedWithKey = await owner.PutAsJsonAsync("/api/model",
            new { provider = "OpenAI", model = "gpt-4.1", baseUrl = "https://proxy.example/v1", apiKey = ApiKey }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(HttpStatusCode.OK, movedWithKey.StatusCode);
        Assert.Equal("https://proxy.example/v1", _kagent.ModelSpec!["openAI"]!["baseUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task SwitchingKeyedProviders_LeavesNoStaleKeyInKAgentsSecret()
    {
        _kagent.ModelSpec = new JsonObject
        {
            ["provider"] = "OpenAI", ["model"] = "gpt-4o", ["apiKeySecret"] = "default-model-config", ["apiKeySecretKey"] = "OPENAI_API_KEY"
        };
        _kagent.Secret["OPENAI_API_KEY"] = "sk-old-openai";
        var owner = await _host.OwnerAsync();

        var response = await owner.PutAsJsonAsync("/api/model", new { provider = "Anthropic", model = "claude-sonnet-4-5", apiKey = ApiKey }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = ApiKey }, _kagent.Secret);
        var puts = _kagent.CallsTo("PUT", FakeKAgent.ModelConfigPath).ToList();
        Assert.Equal(2, puts.Count);
        Assert.Null(puts[0].Body!["apiKey"]);
        Assert.Equal(ApiKey, puts[1].Body!["apiKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task ACreateThatLosesTheRace_IsSavedAsAnUpdate()
    {
        _kagent.CreatedMeanwhile = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3", ["ollama"] = new JsonObject { ["host"] = "http://a:11434" } };
        var owner = await _host.OwnerAsync();

        var response = await owner.PutAsJsonAsync("/api/model", new { provider = "Ollama", model = "qwen3-coder:30b", baseUrl = "http://b:11434" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_kagent.CallsTo("POST", "/api/modelconfigs"));
        Assert.Single(_kagent.CallsTo("PUT", FakeKAgent.ModelConfigPath));
        Assert.Equal("""{"provider":"Ollama","model":"qwen3-coder:30b","ollama":{"host":"http://b:11434"}}""", _kagent.ModelSpec!.ToJsonString());
    }

    [Fact]
    public async Task ModelPage_KeepsPasswordManagersOut_AndNeverRendersTheKeyBack()
    {
        var page = await (await _owner.GetAsync("/Model")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var input = System.Text.RegularExpressions.Regex.Match(page, "<input name=\"ApiKey\"[^>]*>").Value;
        Assert.Contains("type=\"password\"", input);
        Assert.Contains("autocomplete=\"new-password\"", input);
        Assert.Contains("data-1p-ignore", input);
        Assert.Contains("data-lpignore=\"true\"", input);
        Assert.Contains("data-form-type=\"other\"", input);

        var refused = await _owner.SubmitAsync("/Model", new()
        {
            ["Provider"] = "OpenAI", ["ModelId"] = "gpt 4", ["BaseUrl"] = "", ["ApiKey"] = ApiKey
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("must be a model id", body);
        Assert.DoesNotContain(ApiKey, body);
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
    public async Task Investigate_NeverTalksToAnAgent()
    {
        var (employee, _) = await EmployeeAsync();

        var created = await employee.PostAsJsonAsync("/api/investigate", new { query = "What is in my personal memory?" }, cancellationToken: TestContext.Current.CancellationToken);
        var shared = await (await employee.GetAsync("/api/investigate", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.DoesNotContain(_kagent.Calls, c => c.Path.StartsWith("/api/a2a/", StringComparison.Ordinal) || c.Path.StartsWith("/api/sessions", StringComparison.Ordinal));
        Assert.DoesNotContain(_kagent.Calls, c => c.HeaderUser is { Length: > 0 });
        Assert.DoesNotContain(_kagent.Answer, shared);
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
        var model = await owner.SendAsync(Foreign(HttpMethod.Get, "/api/model"), TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task Settings_AreTheOwnersOnly_ExceptReadingTheTheme()
    {
        var owner = await _host.OwnerAsync();
        var (employee, employeeBrowser) = await EmployeeAsync();
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/theme", "light", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync("/api/settings/theme", "dark", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync("/api/settings", new Dictionary<string, string> { ["x"] = "y" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.DeleteAsync("/api/settings/theme", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/settings", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/settings/general.orgName", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/settings/connections", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/settings/connections", new { name = "x", baseUrl = "h" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/settings/layouts", new { name = "x" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/settings/system-info", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("light", (await (await employee.GetAsync("/api/settings/theme", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Trim('"'));
        Assert.Equal("light", (await (await owner.GetAsync("/api/settings/theme", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Trim('"'));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().GetAsync("/api/settings/theme", TestContext.Current.CancellationToken)).StatusCode);
        var page = await employeeBrowser.GetAsync("/Settings");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Login", page.Headers.Location?.OriginalString);
        // Debug reads the owner-only system info, so it is in the owner's nav only.
        var employeeNav = await (await employeeBrowser.GetAsync("/Chat")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var ownerNav = await (await _owner.GetAsync("/Chat")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("href=\"/Hooks\"", employeeNav);
        Assert.DoesNotContain("href=\"/Debug\"", employeeNav);
        Assert.DoesNotContain("href=\"/Settings\"", employeeNav);
        Assert.Contains("href=\"/Debug\"", ownerNav);
    }

    [Fact]
    public async Task ConnectionTokens_AreWriteOnly_AndNamesAreRenderedAsText()
    {
        const string token = "kagent-token-SECRET-0123";
        const string script = "<img src=x onerror=alert(1)>";
        var owner = await _host.OwnerAsync();

        var created = await owner.PostAsJsonAsync("/api/settings/connections", new { name = script, baseUrl = "kagent.local", port = 8083, protocol = "http", token }, cancellationToken: TestContext.Current.CancellationToken);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetInt32();
        var renamed = await owner.PutAsJsonAsync($"/api/settings/connections/{id}", new { name = script + "2", baseUrl = "kagent.local", port = 8083, protocol = "http" }, cancellationToken: TestContext.Current.CancellationToken);
        var list = await owner.GetAsync("/api/settings/connections", TestContext.Current.CancellationToken);
        var listed = await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.DoesNotContain(token, await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(token, await renamed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(token, listed);
        var connection = (await list.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == id);
        Assert.True(connection.GetProperty("hasToken").GetBoolean()); // the update without a token kept it
        Assert.Equal(script + "2", connection.GetProperty("name").GetString());

        // The page builds the list with textContent; no stored value goes through innerHTML.
        var settingsPage = await (await _owner.GetAsync("/Settings")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(".innerHTML", settingsPage);
        Assert.Contains("textContent", settingsPage);
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
