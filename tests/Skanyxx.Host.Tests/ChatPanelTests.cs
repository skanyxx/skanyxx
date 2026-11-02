using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The Chat panel end to end against <see cref="FakeKAgent"/>: the agent picker and details, the person's own
/// conversations (list, reopen, rename, delete), streamed turns with redacted tool steps, stop, files, the quota hook
/// and the deep links. Another person's conversation or file is "not found" on every route.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ChatPanelTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string EmployeeEmail = "eve@skanyxx.example";
    private const string EmployeePassword = "a long employee passphrase";
    private const int MaxTextBytes = 4096;
    private const int MaxFilesPerPerson = 8;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient? _employee;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["KAgent:ChatTimeoutSeconds"] = "20";
            s["Analytics:Prices:kagent/default-model-config:InputPerMillion"] = "2.5";
            s["Analytics:Prices:kagent/default-model-config:OutputPerMillion"] = "10";
            s["Analytics:Currency"] = "EUR";
            s["Chat:Files:MaxTextBytes"] = MaxTextBytes.ToString();
            s["Chat:Files:MaxFilesPerPerson"] = MaxFilesPerPerson.ToString();
        }, services => services.AddScoped<IChatQuota, TestQuota>());
        AddAgents();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    // helper: merged, reads images, tools from two servers and an agent; paused: merged but suspended; hidden: not merged.
    private void AddAgents()
    {
        _kagent.ToolServers["kagent/docs-mcp"] = new JsonObject();
        _kagent.DiscoveredTools["kagent/docs-mcp"] = ["search_docs", "read_doc"];
        _kagent.ToolDescriptions["search_docs"] = "Searches the company docs";
        _kagent.AgentModels["kagent/helper"] = "gpt-4o";
        _kagent.AgentObjects["kagent/helper"] = Agent("helper", new JsonObject { ["skanyxx.dev/merged"] = "true", ["skanyxx.dev/accepts-images"] = "true" },
            new JsonObject
            {
                ["type"] = "Declarative",
                ["description"] = "Answers refund questions",
                ["declarative"] = new JsonObject
                {
                    ["modelConfig"] = "default-model-config",
                    ["systemMessage"] = "SYSTEM-PROMPT-NOT-FOR-CHAT",
                    ["tools"] = new JsonArray(
                        new JsonObject { ["type"] = "McpServer", ["mcpServer"] = new JsonObject { ["name"] = "docs-mcp", ["kind"] = "RemoteMCPServer", ["toolNames"] = new JsonArray("search_docs") } },
                        new JsonObject { ["type"] = "Agent", ["agent"] = new JsonObject { ["name"] = "seed" } }),
                    ["a2aConfig"] = new JsonObject { ["skills"] = new JsonArray(new JsonObject { ["id"] = "refunds", ["name"] = "Refunds", ["description"] = "Refund rules" }) }
                },
                ["skills"] = new JsonObject { ["refs"] = new JsonArray("ghcr.io/acme/skills/refunds:1") }
            });
        _kagent.AgentObjects["kagent/paused"] = Agent("paused", new JsonObject { ["skanyxx.dev/merged"] = "true" },
            new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["deployment"] = new JsonObject { ["replicas"] = 0 } } });
        // D557: an agent made straight in kagent (kagent UI, kubectl, Helm) is offered; Skanyxx's own machinery is not.
        _kagent.AgentObjects["kagent/kagent-made"] = Agent("kagent-made", new JsonObject(), new JsonObject { ["type"] = "Declarative" });
        _kagent.AgentObjects["kagent/hidden"] = Agent("hidden", new JsonObject { ["skanyxx.dev/preview"] = "7", ["skanyxx.dev/managed-by"] = "skanyxx-studio" },
            new JsonObject { ["type"] = "Declarative" });
        _kagent.AgentObjects["kagent/factory"] = Agent("factory", new JsonObject { ["skanyxx.dev/role"] = "factory" }, new JsonObject { ["type"] = "Declarative" });
    }

    private static JsonObject Agent(string name, JsonObject labels, JsonObject spec) => new()
    {
        ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = "kagent", ["labels"] = labels },
        ["spec"] = spec
    };

    // ---------- agents ----------

    [Fact]
    public async Task ThePicker_ListsOnlyTheAgentsChatOffers_WithTheirModel()
    {
        var employee = await EmployeeAsync();

        var agents = await employee.GetFromJsonAsync<JsonElement>("/api/chat/agents", Ct);

        Assert.Equal(["kagent/e2e-hello", "kagent/helper", "kagent/kagent-made", "kagent/seed", "team-a/seed"],
            agents.EnumerateArray().Select(a => $"{a.GetProperty("namespace").GetString()}/{a.GetProperty("name").GetString()}"));
        var helper = agents.EnumerateArray().First(a => a.GetProperty("name").GetString() == "helper");
        Assert.Equal("gpt-4o", helper.GetProperty("model").GetString());
        Assert.True(helper.GetProperty("acceptsImages").GetBoolean());
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("factory")]
    [InlineData("paused")]
    [InlineData("ticket-planner")]
    public async Task AnAgentChatDoesNotOffer_HasNoDetails_AndNoTurn(string name)
    {
        var employee = await EmployeeAsync();

        var details = await employee.GetAsync($"/api/chat/agents/kagent/{name}", Ct);
        var turn = await employee.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = name, message = "hi" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, turn.StatusCode);
        Assert.Empty(_kagent.CallsTo("POST", "/api/sessions"));
    }

    /// <summary>D557: an agent nobody labelled (made in kagent directly) has details and takes an employee's turn.</summary>
    [Fact]
    public async Task AnUnlabelledAgent_HasDetails_AndAnEmployeeChatsWithIt()
    {
        var employee = await EmployeeAsync();

        var details = await employee.GetAsync("/api/chat/agents/kagent/kagent-made", Ct);
        var turn = await employee.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "kagent-made", message = "hi" }, Ct);

        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
        Assert.Equal("kagent/kagent-made", Assert.Single(_kagent.CallsTo("POST", "/api/sessions")).Body!["agent_ref"]!.GetValue<string>());
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/kagent/kagent-made/"));
    }

    [Fact]
    public async Task AgentDetails_OfAnUnpricedModel_HaveNoPrice()
    {
        var employee = await EmployeeAsync();
        _kagent.AgentObjects["kagent/local"] = Agent("local", new JsonObject { ["skanyxx.dev/merged"] = "true" },
            new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["modelConfig"] = "ollama-local", ["systemMessage"] = "x" } });

        var details = await employee.GetFromJsonAsync<JsonElement>("/api/chat/agents/kagent/local", Ct);

        Assert.Equal(JsonValueKind.Null, details.GetProperty("price").ValueKind);
    }

    [Fact]
    public async Task AgentDetails_ShowModelToolsSkillsAndMemory_NeverTheSystemPrompt()
    {
        var employee = await EmployeeAsync();

        var response = await employee.GetAsync("/api/chat/agents/kagent/helper", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var details = JsonDocument.Parse(body).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("SYSTEM-PROMPT-NOT-FOR-CHAT", body);
        Assert.Equal("Answers refund questions", details.GetProperty("description").GetString());
        Assert.Equal("gpt-4o", details.GetProperty("model").GetString());
        Assert.Equal("kagent/default-model-config", details.GetProperty("modelConfig").GetString());
        // D549: the configured price of the agent's model, for Chat's estimate.
        var price = details.GetProperty("price");
        Assert.Equal("kagent/default-model-config", price.GetProperty("model").GetString());
        Assert.Equal(2.5m, price.GetProperty("inputPerMillion").GetDecimal());
        Assert.Equal(10m, price.GetProperty("outputPerMillion").GetDecimal());
        Assert.Equal("EUR", price.GetProperty("currency").GetString());
        var server = Assert.Single(details.GetProperty("toolServers").EnumerateArray());
        Assert.Equal("kagent/docs-mcp", server.GetProperty("ref").GetString());
        Assert.False(server.GetProperty("allTools").GetBoolean());
        var tool = Assert.Single(server.GetProperty("tools").EnumerateArray());
        Assert.Equal("search_docs", tool.GetProperty("name").GetString());
        Assert.Equal("Searches the company docs", tool.GetProperty("description").GetString());
        Assert.Equal(["kagent/seed"], details.GetProperty("agentTools").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(["ghcr.io/acme/skills/refunds:1"], details.GetProperty("skills").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("Refunds", Assert.Single(details.GetProperty("a2aSkills").EnumerateArray()).GetProperty("name").GetString());
        // Memory is installed and the agent has no grants of its own: the default.
        Assert.Empty(details.GetProperty("memory").EnumerateArray());
        Assert.Equal("/Agents?agent=kagent%2Fhelper", details.GetProperty("agentsPageUrl").GetString());
    }

    // ---------- conversations ----------

    [Fact]
    public async Task Conversations_AreListedRenamedAndDeleted_ByTheirOwnerOnly()
    {
        var owner = await _host.OwnerAsync();
        var employee = await EmployeeAsync();
        var first = await ChatAsync(owner, "Why are EU refunds slow?\nmore detail");
        var second = await ChatAsync(owner, "Gift card rules", agent: "helper");
        var theirs = await ChatAsync(employee, "My own question");

        var mine = await owner.GetFromJsonAsync<JsonElement>("/api/chat/conversations", Ct);
        Assert.Equal([second, first], mine.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        Assert.Equal("Why are EU refunds slow?", mine.EnumerateArray().Last().GetProperty("title").GetString());
        Assert.Equal("helper", mine.EnumerateArray().First().GetProperty("agentName").GetString());
        Assert.Equal([theirs], (await employee.GetFromJsonAsync<JsonElement>("/api/chat/conversations", Ct)).EnumerateArray().Select(c => c.GetProperty("id").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await employee.PatchAsJsonAsync($"/api/chat/conversations/{first}", new { title = "taken" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.DeleteAsync($"/api/chat/conversations/{first}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/chat/conversations/{first}", new { title = "two\nlines" }, Ct)).StatusCode);

        var renamed = await owner.PatchAsJsonAsync($"/api/chat/conversations/{first}", new { title = "EU refunds" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("EU refunds", (await owner.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{first}", Ct)).GetProperty("title").GetString());
        // The chat index (history search, ticket views) follows.
        Assert.Equal("EU refunds", (await owner.GetFromJsonAsync<JsonElement>($"/api/chat/sessions/{first}", Ct)).GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/chat/conversations/{first}", Ct)).StatusCode);
        Assert.DoesNotContain(first, _kagent.SessionIds);
        Assert.Equal([second], (await owner.GetFromJsonAsync<JsonElement>("/api/chat/conversations", Ct)).EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/chat/sessions/{first}", Ct)).StatusCode);
        Assert.Contains(theirs, _kagent.SessionIds);
    }

    [Fact]
    public async Task AnotherPersonsConversation_IsNotFound_OnEveryRoute()
    {
        var owner = await _host.OwnerAsync();
        var employee = await EmployeeAsync();
        var id = await ChatAsync(owner, "Private question");

        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/chat/conversations/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.PatchAsJsonAsync($"/api/chat/conversations/{id}", new { title = "mine now" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.DeleteAsync($"/api/chat/conversations/{id}", Ct)).StatusCode);
        var turn = await employee.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = "seed", message = "continue", conversationId = id }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, turn.StatusCode);
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/kagent/seed/"));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/chat/conversations/{id}", Ct)).StatusCode);
    }

    /// <summary>requirements.md §3.4: a reload continues the same session — read back from kagent, never a new one.</summary>
    [Fact]
    public async Task AReload_ReopensTheSameConversation_WithItsAnswerAndTokens_NoSteps_AndCreatesNoSession()
    {
        var owner = await _host.OwnerAsync();
        _kagent.StreamToolCall = true;
        var (events, _) = await StreamAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "What is our refund window?" });
        var id = events.First(e => e.Type == "session").Data.GetProperty("conversationId").GetString()!;
        var browser = await SignedInBrowserAsync();

        var page = await browser.GetAsync($"/Chat/kagent/seed/{id}");
        var view = await owner.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{id}", Ct);
        var again = await owner.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains($"data-session=\"{id}\"", await page.Content.ReadAsStringAsync(Ct));
        Assert.Single(_kagent.CallsTo("POST", "/api/sessions"));
        Assert.Equal(view.GetRawText(), again.GetRawText());
        // D555: no tool steps, not even what the model said on its way to the tool.
        Assert.Equal(["user", "assistant"], view.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()));
        Assert.DoesNotContain("memory_search", view.GetRawText());
        Assert.DoesNotContain("Let me look that up.", view.GetRawText());
        var items = view.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("What is our refund window?", items[0].GetProperty("text").GetString());
        Assert.Equal(_kagent.Answer, items[1].GetProperty("text").GetString());
        // The answer carries its whole turn's tokens (the tool-calling model call and the answer).
        Assert.Equal(180, items[1].GetProperty("usage").GetProperty("total").GetInt64());
        Assert.Equal(180, view.GetProperty("usage").GetProperty("total").GetInt64());

        // The next message continues it.
        var (next, _) = await StreamAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "And gift cards?", conversationId = id });
        Assert.False(next.First(e => e.Type == "session").Data.GetProperty("isNew").GetBoolean());
        Assert.Single(_kagent.CallsTo("POST", "/api/sessions"));
    }

    // ---------- streamed turns ----------

    [Fact]
    public async Task AStreamedTurn_SendsNoToolSteps_OnlyTheAnswerWithItsTokens()
    {
        var owner = await _host.OwnerAsync();
        _kagent.StreamToolCall = true;

        var (events, raw) = await StreamAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "What is our refund window?" });

        Assert.DoesNotContain(FakeKAgent.StreamSecret, raw);
        Assert.Equal("session", events[0].Type);
        Assert.True(events[0].Data.GetProperty("isNew").GetBoolean());
        Assert.Equal("What is our refund window?", events[0].Data.GetProperty("title").GetString());
        // D555: the tool call, its result and what the model said on its way there never leave the server.
        Assert.DoesNotContain(events, e => e.Type == "item");
        Assert.DoesNotContain("memory_search", raw);
        Assert.DoesNotContain("Let me look that up.", raw);
        Assert.Contains(events, e => e.Type == "delta");
        Assert.Equal(_kagent.Answer, events.Last(e => e.Type == "answer").Data.GetProperty("text").GetString());
        Assert.DoesNotContain(events, e => e.Type == "answer" && e.Data.GetProperty("text").GetString()!.Contains("thinking"));
        var done = events.Last();
        Assert.Equal("done", done.Type);
        Assert.Equal(180, done.Data.GetProperty("usage").GetProperty("total").GetInt64());

        // Read back, no steps either.
        var id = events[0].Data.GetProperty("conversationId").GetString();
        var reopened = await owner.GetStringAsync($"/api/chat/conversations/{id}", Ct);
        Assert.DoesNotContain(FakeKAgent.StreamSecret, reopened);
        Assert.DoesNotContain("memory_search", reopened);
    }

    [Fact]
    public async Task AFailedFirstTurn_IsAnErrorInPlainWords_AndLeavesNoConversation()
    {
        var employee = await EmployeeAsync();
        _kagent.StreamFinalState = "failed";

        var (events, raw) = await StreamAsync(employee, new { agentNamespace = "kagent", agentName = "seed", message = "hi" });

        var error = events.Last();
        Assert.Equal("error", error.Type);
        Assert.Equal("The agent could not finish the answer. Try again in a moment.", error.Data.GetProperty("message").GetString());
        Assert.True(error.Data.GetProperty("conversationGone").GetBoolean());
        Assert.False(error.Data.TryGetProperty("kagentReason", out _));
        Assert.DoesNotContain("SECRET-INTERNAL", raw);
        Assert.Empty(_kagent.SessionIds);
    }

    /// <summary>D553: kagent's reason goes to builders and up, cleaned, under the same fixed words; never to an employee.</summary>
    [Fact]
    public async Task AFailedStreamedTurn_CarriesKagentsReason_ToBuildersAndUpOnly()
    {
        _kagent.StreamFinalState = "failed";
        _kagent.StreamFailureText = $"{ChatFailureReasonTests.Observed} Authorization: Bearer {FakeKAgent.StreamSecret}";
        var builder = await PersonAsync("bo@skanyxx.example", "builder");
        var supervisor = await PersonAsync("sue@skanyxx.example", "supervisor");

        foreach (var client in new[] { await _host.OwnerAsync(), supervisor, builder })
        {
            var (events, raw) = await StreamAsync(client, new { agentNamespace = "kagent", agentName = "seed", message = "hi" });
            var error = events.Last();
            Assert.Equal("The agent could not finish the answer. Try again in a moment.", error.Data.GetProperty("message").GetString());
            Assert.StartsWith(ChatFailureReasonTests.Observed, error.Data.GetProperty("kagentReason").GetString());
            Assert.DoesNotContain(FakeKAgent.StreamSecret, raw);
        }
        var (asEmployee, employeeRaw) = await StreamAsync(await EmployeeAsync(), new { agentNamespace = "kagent", agentName = "seed", message = "hi" });
        Assert.False(asEmployee.Last().Data.TryGetProperty("kagentReason", out _));
        Assert.DoesNotContain("host.docker.internal", employeeRaw);
    }

    [Fact]
    public async Task AFailedBlockingTurn_CarriesKagentsReason_ToBuildersAndUpOnly()
    {
        _kagent.A2ABody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["kind"] = "task", ["id"] = "task-7",
                ["status"] = new JsonObject
                {
                    ["state"] = "failed",
                    ["message"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["kind"] = "text", ["text"] = ChatFailureReasonTests.Observed }) }
                }
            }
        }.ToJsonString();

        var asOwner = await (await _host.OwnerAsync()).PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, Ct);
        var asBuilder = await (await PersonAsync("bo@skanyxx.example", "builder")).PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, Ct);
        var asEmployee = await (await EmployeeAsync()).PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, asOwner.StatusCode);
        var problem = await asOwner.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("The agent could not finish the answer. Try again in a moment.", problem.GetProperty("detail").GetString());
        Assert.Equal(ChatFailureReasonTests.Observed, problem.GetProperty("kagentReason").GetString());
        Assert.Equal(ChatFailureReasonTests.Observed, (await asBuilder.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("kagentReason").GetString());
        Assert.Equal(HttpStatusCode.BadGateway, asEmployee.StatusCode);
        var employeeBody = await asEmployee.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("host.docker.internal", employeeBody);
        Assert.DoesNotContain("kagentReason", employeeBody);
        Assert.Empty(_kagent.SessionIds);
    }

    [Fact]
    public async Task ABlockingApprovalStop_AsksForTheApproval_WithNoKagentReason()
    {
        _kagent.A2ABody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["kind"] = "task", ["id"] = "task-8",
                ["status"] = new JsonObject
                {
                    ["state"] = "auth-required",
                    ["message"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["kind"] = "text", ["text"] = "Sign in at https://idp.example/?state=s3cr3t" }) }
                }
            }
        }.ToJsonString();

        var response = await (await _host.OwnerAsync()).PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("approval", body);
        Assert.DoesNotContain("kagentReason", body);
        Assert.DoesNotContain("idp.example", body);
    }

    /// <summary>D554: Ready in kagent, but a tool server it names gives no tools: the picker and details say so.</summary>
    [Fact]
    public async Task AnAgentWhoseToolServerGivesNoTools_ShowsUnhealthy_ByNameForBuildersAndUpOnly()
    {
        _kagent.AgentObjects["kagent/broken"] = Agent("broken", new JsonObject { ["skanyxx.dev/merged"] = "true" }, new JsonObject
        {
            ["type"] = "Declarative",
            ["declarative"] = new JsonObject
            {
                ["tools"] = new JsonArray(
                    new JsonObject { ["type"] = "McpServer", ["mcpServer"] = new JsonObject { ["name"] = "skanyxx-memory-seed", ["kind"] = "RemoteMCPServer" } },
                    new JsonObject { ["type"] = "McpServer", ["mcpServer"] = new JsonObject { ["name"] = "gone-mcp", ["namespace"] = "team-a", ["kind"] = "RemoteMCPServer" } })
            }
        });
        var owner = await _host.OwnerAsync();
        var employee = await EmployeeAsync();
        var builder = await PersonAsync("bo@skanyxx.example", "builder");

        var picker = (await owner.GetFromJsonAsync<JsonElement>("/api/chat/agents", Ct)).EnumerateArray().ToList();
        var broken = picker.First(a => a.GetProperty("name").GetString() == "broken");
        Assert.Equal($"kagent/skanyxx-memory-seed: {ToolServerHealth.NoTools} team-a/gone-mcp: {ToolServerHealth.NotListed}", broken.GetProperty("toolProblem").GetString());
        Assert.Equal(JsonValueKind.Null, picker.First(a => a.GetProperty("name").GetString() == "helper").GetProperty("toolProblem").ValueKind);
        Assert.Equal(broken.GetProperty("toolProblem").GetString(), (await builder.GetFromJsonAsync<JsonElement>("/api/chat/agents", Ct)).EnumerateArray()
            .First(a => a.GetProperty("name").GetString() == "broken").GetProperty("toolProblem").GetString());
        var details = await owner.GetFromJsonAsync<JsonElement>("/api/chat/agents/kagent/broken", Ct);
        Assert.Equal(broken.GetProperty("toolProblem").GetString(), details.GetProperty("toolProblem").GetString());
        Assert.Equal([ToolServerHealth.NoTools, ToolServerHealth.NotListed],
            details.GetProperty("toolServers").EnumerateArray().Select(s => s.GetProperty("problem").GetString()));

        var asEmployee = (await employee.GetFromJsonAsync<JsonElement>("/api/chat/agents", Ct)).EnumerateArray().First(a => a.GetProperty("name").GetString() == "broken");
        Assert.Equal(ToolServerHealth.ForEmployees, asEmployee.GetProperty("toolProblem").GetString());
        var employeeDetails = await employee.GetFromJsonAsync<JsonElement>("/api/chat/agents/kagent/broken", Ct);
        Assert.Equal(ToolServerHealth.ForEmployees, employeeDetails.GetProperty("toolProblem").GetString());
        Assert.All(employeeDetails.GetProperty("toolServers").EnumerateArray(), s => Assert.Equal(ToolServerHealth.Unavailable, s.GetProperty("problem").GetString()));
    }

    [Fact]
    public async Task Stop_CancelsKagentsTask_AndAStoppedFirstTurnLeavesNoConversation()
    {
        var owner = await _host.OwnerAsync();
        _kagent.StreamToolCall = true;
        _kagent.StreamGate = new TaskCompletionSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new { agentNamespace = "kagent", agentName = "seed", message = "Long question" })
        };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var response = await owner.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop.Token));
        while (await reader.ReadLineAsync(stop.Token) is { } line && line != "event: usage") { }
        await stop.CancelAsync();
        response.Dispose();

        Assert.True(await UntilAsync(() => _kagent.CallsTo("POST", "/api/a2a/kagent/seed/").Any(c => c.Body?["method"]?.GetValue<string>() == "tasks/cancel")),
            "kagent was not asked to cancel the task");
        Assert.True(await UntilAsync(() => _kagent.SessionIds.Count == 0), "the stopped first turn's conversation was kept");
    }

    [Fact]
    public async Task TheQuotaHook_RefusesATurn_BeforeKagent()
    {
        var owner = await _host.OwnerAsync();
        TestQuota.Refuse = true;
        try
        {
            var response = await owner.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = "seed", message = "hi" }, Ct);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Contains(TestQuota.Refusal, await response.Content.ReadAsStringAsync(Ct));
            Assert.Empty(_kagent.CallsTo("POST", "/api/sessions"));
        }
        finally
        {
            TestQuota.Refuse = false;
        }
    }

    // ---------- files ----------

    [Fact]
    public async Task Uploads_AreCheckedByTypeContentAndSize_AndSecretsAreFlagged()
    {
        var owner = await _host.OwnerAsync();

        var secret = await UploadAsync(owner, "notes.txt", "Deploy notes\npassword: hunter2hunter2\n"u8.ToArray());
        var clean = await UploadAsync(owner, "plan.md", "# Plan\nShip it."u8.ToArray());
        var exe = await UploadAsync(owner, "tool.exe", [0x4D, 0x5A, 0x90]);
        var fakePng = await UploadAsync(owner, "photo.png", "not an image"u8.ToArray());
        var binaryText = await UploadAsync(owner, "data.txt", [0x41, 0x00, 0x42]);
        var tooBig = await UploadAsync(owner, "big.txt", Encoding.UTF8.GetBytes(new string('a', MaxTextBytes + 1)));

        Assert.Equal(HttpStatusCode.Created, secret.StatusCode);
        var info = await secret.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(info.GetProperty("secretsFound").GetBoolean());
        Assert.Contains("secrets", info.GetProperty("warning").GetString());
        Assert.False((await clean.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("secretsFound").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, exe.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fakePng.StatusCode);
        Assert.Contains("not a .png image", await fakePng.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, binaryText.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        // Kept on disk outside wwwroot.
        Assert.Contains(Directory.EnumerateFiles(Path.Combine(_host.ContentRoot, "data", "chat-files"), "*.bin", SearchOption.AllDirectories),
            f => f.EndsWith(info.GetProperty("id").GetString() + ".bin"));
    }

    [Fact]
    public async Task AFile_IsItsOwnersOnly_AndServedAsADownload()
    {
        var owner = await _host.OwnerAsync();
        var employee = await EmployeeAsync();
        var id = (await (await UploadAsync(owner, "page.html", "<script>alert(1)</script>"u8.ToArray())).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();

        var download = await owner.GetAsync($"/api/chat/files/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/chat/files/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.DeleteAsync($"/api/chat/files/{id}", Ct)).StatusCode);
        var stolen = await employee.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = "seed", message = "read this", fileIds = new[] { id } }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/chat/files/..%2F..%2Fappsettings", Ct)).StatusCode);
    }

    [Fact]
    public async Task ATextFile_GoesToTheAgentRedacted_AndComesBackAsAnAttachment()
    {
        var owner = await _host.OwnerAsync();
        var id = (await (await UploadAsync(owner, "notes.txt", "Deploy notes\npassword: hunter2hunter2\n"u8.ToArray())).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

        var (events, _) = await StreamAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Summarise the file", fileIds = new[] { id } });

        var sent = _kagent.CallsTo("POST", "/api/a2a/kagent/seed/").Last().Body!["params"]!["message"]!["parts"]![0]!["text"]!.GetValue<string>();
        Assert.Contains($"[Attached file \"notes.txt\" ({id})]", sent);
        Assert.Contains("Deploy notes", sent);
        Assert.Contains("[redacted]", sent);
        Assert.DoesNotContain("hunter2hunter2", sent);
        Assert.EndsWith("Summarise the file", sent);
        var conversation = events[0].Data.GetProperty("conversationId").GetString();
        var user = (await owner.GetFromJsonAsync<JsonElement>($"/api/chat/conversations/{conversation}", Ct)).GetProperty("items")[0];
        Assert.Equal("Summarise the file", user.GetProperty("text").GetString());
        Assert.Equal(id, Assert.Single(user.GetProperty("attachments").EnumerateArray()).GetProperty("id").GetString());

        // Deleting the conversation deletes its files.
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/chat/conversations/{conversation}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/chat/files/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task AnImage_GoesOnlyToAnAgentThatReadsImages_ElseTheRefusalSaysWhy()
    {
        var owner = await _host.OwnerAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var id = (await (await UploadAsync(owner, "chart.png", png)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

        var toSeed = await owner.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = "seed", message = "What does it show?", fileIds = new[] { id } }, Ct);
        var (events, _) = await StreamAsync(owner, new { agentNamespace = "kagent", agentName = "helper", message = "What does it show?", fileIds = new[] { id } });

        Assert.Equal(HttpStatusCode.Conflict, toSeed.StatusCode);
        Assert.Contains("does not read images", await toSeed.Content.ReadAsStringAsync(Ct));
        Assert.Equal("done", events.Last().Type);
        var file = _kagent.CallsTo("POST", "/api/a2a/kagent/helper/").Last().Body!["params"]!["message"]!["parts"]![1]!;
        Assert.Equal("file", file["kind"]!.GetValue<string>());
        Assert.Equal("image/png", file["file"]!["mimeType"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(png), file["file"]!["bytes"]!.GetValue<string>());
    }

    [Fact]
    public async Task APerson_KeepsALimitedNumberOfFiles()
    {
        var owner = await _host.OwnerAsync();
        for (var i = 0; i < MaxFilesPerPerson; i++)
            Assert.Equal(HttpStatusCode.Created, (await UploadAsync(owner, $"n{i}.txt", "note"u8.ToArray())).StatusCode);

        var refused = await UploadAsync(owner, "one-more.txt", "note"u8.ToArray());
        var employee = await UploadAsync(await EmployeeAsync(), "theirs.txt", "note"u8.ToArray());

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Delete a conversation", await refused.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Created, employee.StatusCode);
    }

    [Fact]
    public async Task ParallelUploads_AllSucceed_UpToTheCap_AndNeverPastIt()
    {
        var owner = await _host.OwnerAsync();

        var within = await Task.WhenAll(Enumerable.Range(0, MaxFilesPerPerson).Select(i => UploadAsync(owner, $"n{i}.txt", "note"u8.ToArray())));
        Assert.All(within, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var employee = await EmployeeAsync();
        var burst = await Task.WhenAll(Enumerable.Range(0, MaxFilesPerPerson * 2).Select(i => UploadAsync(employee, $"b{i}.txt", "note"u8.ToArray())));
        Assert.Equal(MaxFilesPerPerson, burst.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(burst, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.TooManyRequests }));
        Assert.Equal(MaxFilesPerPerson * 2, StoredFiles("*.bin").Length);
        Assert.Equal(MaxFilesPerPerson * 2, StoredFiles("*.json").Length);
        Assert.Empty(StoredFiles("*.tmp"));
    }

    [Fact]
    public async Task ATornRecord_BreaksNeitherUploadNorSendNorDelete_AndADeletedConversationTakesItsFiles()
    {
        var owner = await _host.OwnerAsync();
        var id = await FileIdAsync(await UploadAsync(owner, "notes.txt", "Deploy notes"u8.ToArray()));
        var folder = Path.GetDirectoryName(Assert.Single(StoredFiles("*.bin")))!;
        var torn = Path.Combine(folder, new string('a', 32) + ".json");
        await File.WriteAllTextAsync(torn, "{\"id\":\"aaaa", Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, new string('b', 32) + ".json"), "", Ct);

        var second = await UploadAsync(owner, "more.txt", "More notes"u8.ToArray());
        var (events, _) = await StreamAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Summarise", fileIds = new[] { id } });
        var conversation = events[0].Data.GetProperty("conversationId").GetString();
        var removed = await owner.DeleteAsync($"/api/chat/files/{await FileIdAsync(second)}", Ct);
        var deleted = await owner.DeleteAsync($"/api/chat/conversations/{conversation}", Ct);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("done", events.Last().Type);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/chat/files/{id}", Ct)).StatusCode);
        Assert.Empty(StoredFiles("*.bin"));
        Assert.Equal([torn, Path.Combine(folder, new string('b', 32) + ".json")], StoredFiles("*.json").Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/chat/conversations/{conversation}", Ct)).StatusCode);
    }

    [Fact]
    public async Task ADeletedConversation_LeavesTheChatIndex_EvenWhenAFileCannotBeRemoved()
    {
        var owner = await _host.OwnerAsync();
        var id = await FileIdAsync(await UploadAsync(owner, "notes.txt", "Deploy notes"u8.ToArray()));
        var (events, _) = await StreamAsync(owner, new { agentNamespace = "kagent", agentName = "seed", message = "Summarise", fileIds = new[] { id } });
        var conversation = events[0].Data.GetProperty("conversationId").GetString()!;
        for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline && !(await IndexedAsync(owner)).Contains(conversation);)
            await Task.Delay(50, Ct);
        Assert.Contains(conversation, await IndexedAsync(owner));
        // A folder where the bytes were: removing it throws, as a read-only or failing volume would.
        var bytes = Assert.Single(StoredFiles("*.bin"));
        File.Delete(bytes);
        Directory.CreateDirectory(Path.Combine(bytes, "busy"));

        var deleted = await owner.DeleteAsync($"/api/chat/conversations/{conversation}", Ct);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.DoesNotContain(conversation, await IndexedAsync(owner));
    }

    [Fact]
    public async Task AFileWhoseBytesAreGone_Is404_NotABrokenDownload()
    {
        var owner = await _host.OwnerAsync();
        var id = await FileIdAsync(await UploadAsync(owner, "notes.txt", "Deploy notes"u8.ToArray()));
        File.Delete(Assert.Single(StoredFiles("*.bin")));

        var download = await owner.GetAsync($"/api/chat/files/{id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal("application/problem+json", download.Content.Headers.ContentType?.MediaType);
        Assert.Null(download.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task AnUploadOverTheLargestKindsLimit_IsRefusedByTheBodyCap_AndOneOverItsOwnKindsLimit_IsA400()
    {
        var owner = await _host.OwnerAsync();
        const int imageLimit = 4 * 1024 * 1024; // the default; text is 4 KB here, so the cap is the image limit, not the 20 MB ceiling

        // Only the first 64 KB of the body is sent: the answer has to come without the rest, from the declared length.
        var overCap = await DeclaredUploadAsync(owner, imageLimit + 128 * 1024, sent: 64 * 1024);
        var overText = await UploadAsync(owner, "big.txt", Encoding.UTF8.GetBytes(new string('a', 64 * 1024)));

        Assert.StartsWith("HTTP/1.1 413 ", overCap);
        Assert.Equal(HttpStatusCode.BadRequest, overText.StatusCode);
        Assert.Contains("Text files may be up to 4 KB", await overText.Content.ReadAsStringAsync(Ct));
        Assert.DoesNotContain("UTF-8", await overText.Content.ReadAsStringAsync(Ct)); // refused on its size, never read for its content
        Assert.Empty(StoredFiles("*.bin"));
    }

    [Fact]
    public async Task TooManyFiles_AreRefused()
    {
        var owner = await _host.OwnerAsync();

        var response = await owner.PostAsJsonAsync("/api/chat/stream", new
        {
            agentNamespace = "kagent", agentName = "seed", message = "hi",
            fileIds = Enumerable.Range(0, 6).Select(i => i.ToString("x32")).ToArray()
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at most 5 files", await response.Content.ReadAsStringAsync(Ct));
    }

    // ---------- the page ----------

    [Fact]
    public async Task TheDeepLink_OpensTheAgent_AndOddRoutesAreNoPage()
    {
        var browser = await SignedInBrowserAsync();

        var agent = await browser.GetAsync("/Chat/kagent/seed");
        var conversation = await browser.GetAsync("/Chat/team-a/seed/abc_DEF-123");

        Assert.Equal(HttpStatusCode.OK, agent.StatusCode);
        var page = await agent.Content.ReadAsStringAsync(Ct);
        Assert.Contains("data-namespace=\"kagent\" data-agent=\"seed\"", page);
        Assert.Contains("data-session=\"\"", page);
        var reopened = await conversation.Content.ReadAsStringAsync(Ct);
        Assert.Contains("data-namespace=\"team-a\" data-agent=\"seed\"", reopened);
        Assert.Contains("data-session=\"abc_DEF-123\"", reopened);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/Chat/Kagent/seed")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/Chat/kagent/seed/not%20an%20id")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await new Browser(_host).GetAsync("/Chat/kagent/seed")).StatusCode);
    }

    [Fact]
    public async Task TheScript_KeepsTheConversationInTheUrl_AndVoiceStaysOnTheDevice()
    {
        var script = await _host.Client().GetStringAsync("/js/chat.js", Ct);

        Assert.Contains("history[push ? 'pushState' : 'replaceState']", script);
        Assert.Contains("sessionStorage.setItem(STORE_KEY", script);
        Assert.Contains("call('GET', `/api/chat/conversations/${encodeURIComponent(id)}`)", script);
        Assert.Contains("fetch('/api/chat/stream'", script);
        Assert.Contains("controller.abort()", script);
        Assert.Contains("recognition.processLocally = true", script);
        Assert.Contains("mic.disabled = true", script);
    }

    // ---------- helpers ----------

    private sealed record StreamEvent(string Type, JsonElement Data);

    private async Task<(List<StreamEvent> Events, string Raw)> StreamAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/chat/stream", body, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        var events = raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Split('\n'))
            .Select(lines => new StreamEvent(lines[0]["event: ".Length..], JsonDocument.Parse(lines[1]["data: ".Length..]).RootElement))
            .ToList();
        return (events, raw);
    }

    private async Task<string> ChatAsync(HttpClient client, string message, string agent = "seed")
    {
        var response = await client.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = agent, message }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("conversationId").GetString()!;
    }

    private static async Task<List<string>> IndexedAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>("/api/chat/sessions?mine=true", Ct)).EnumerateArray().Select(s => s.GetProperty("sessionId").GetString()!)];

    private string[] StoredFiles(string pattern)
    {
        var root = Path.Combine(_host.ContentRoot, "data", "chat-files");
        return Directory.Exists(root) ? Directory.GetFiles(root, pattern, SearchOption.AllDirectories) : [];
    }

    private static async Task<string> FileIdAsync(HttpResponseMessage upload) =>
        (await upload.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        return await client.PostAsync("/api/chat/files", form, Ct);
    }

    /// <summary>
    /// A multipart upload that declares <paramref name="length"/> bytes but sends only <paramref name="sent"/> of them, over a
    /// raw socket (HttpClient would keep writing the rest and lose the answer to the reset that follows it). Returns
    /// the answer's head.
    /// </summary>
    private async Task<string> DeclaredUploadAsync(HttpClient client, int length, int sent)
    {
        const string boundary = "skx-boundary";
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(_host.BaseAddress.Host, _host.BaseAddress.Port, Ct);
        var stream = tcp.GetStream();
        var head = new StringBuilder()
            .Append($"POST /api/chat/files HTTP/1.1\r\nHost: {_host.BaseAddress.Authority}\r\n")
            .Append($"Authorization: {client.DefaultRequestHeaders.Authorization}\r\n")
            .Append($"Content-Type: multipart/form-data; boundary={boundary}\r\nContent-Length: {length}\r\n\r\n")
            .Append($"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"huge.txt\"\r\n")
            .Append("Content-Type: application/octet-stream\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), Ct);
        await stream.WriteAsync(new byte[sent], Ct);

        // A server that waited for the rest would never answer: that is a failure, not a hang.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var answer = new StringBuilder();
        var buffer = new byte[4096];
        while (!answer.ToString().Contains("\r\n\r\n") && await stream.ReadAsync(buffer, timeout.Token) is > 0 and var read)
            answer.Append(Encoding.ASCII.GetString(buffer, 0, read));
        return answer.ToString();
    }

    private async Task<Browser> SignedInBrowserAsync()
    {
        await _host.OwnerAsync();
        var browser = new Browser(_host);
        await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });
        return browser;
    }

    private static async Task<bool> UntilAsync(Func<bool> condition)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline; await Task.Delay(50))
            if (condition())
                return true;
        return condition();
    }

    private async Task<HttpClient> EmployeeAsync() => _employee ??= await PersonAsync(EmployeeEmail, "employee");

    private async Task<HttpClient> PersonAsync(string email, string role)
    {
        var owner = await _host.OwnerAsync();
        var invite = await owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } }, Ct);
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;
        (await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = EmployeePassword }, Ct)).EnsureSuccessStatusCode();
        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email, password = EmployeePassword }, Ct);
        signIn.EnsureSuccessStatusCode();
        return _host.Client((await signIn.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }

    /// <summary>The quota hook, refusing when a test says so (the default lets every turn through).</summary>
    private sealed class TestQuota : IChatQuota
    {
        public const string Refusal = "You used this month's chat allowance. It renews on the 1st.";

        public static volatile bool Refuse;

        public Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct) =>
            Task.FromResult(Refuse ? ChatQuotaAdmission.Refused(Refusal) : ChatQuotaAdmission.Admitted());

        public Task<string?> RefusalAsync(string userId, CancellationToken ct) => Task.FromResult(Refuse ? Refusal : null);

        public Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct) => Task.CompletedTask;
    }
}
