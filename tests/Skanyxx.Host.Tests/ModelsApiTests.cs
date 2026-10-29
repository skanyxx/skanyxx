using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skanyxx.Host.Tests;

/// <summary>
/// <c>api/models</c> and the Models page (D323) on the real host against <see cref="FakeKAgent"/>: the list with the
/// default marked, create per provider, edit that keeps what the form does not own, delete with the agents that use the
/// model, the default that is never deleted, who may do what, and an API key that never comes back or reaches the log.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ModelsApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string ApiKey = "sk-test-0123456789abcdefSECRET";
    private const string Default = FakeKAgent.DefaultModel;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _kagent.ListsAbsentDefault = false;
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
        });
        _owner = await _host.OwnerAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- List ---

    [Fact]
    public async Task List_ShowsEveryModel_TheDefaultMarked_AndOnlyWhetherAKeyIsSet()
    {
        _kagent.ModelSpec = Keyed("OpenAI", "gpt-4o", "default-model-config");
        _kagent.Secret["OPENAI_API_KEY"] = ApiKey;
        _kagent.ModelConfigs["kagent/azure"] = new JsonObject
        {
            ["provider"] = "AzureOpenAI", ["model"] = "gpt-4.1",
            ["azureOpenAI"] = new JsonObject { ["azureEndpoint"] = "https://az.example", ["apiVersion"] = "2024-10-21", ["azureAdToken"] = "AAD-TOKEN-SECRET" },
            ["defaultHeaders"] = new JsonObject { ["Authorization"] = "Bearer HEADER-SECRET" }
        };
        var supervisor = (await PersonAsync("sue@skanyxx.example", "supervisor")).Api;

        var response = await supervisor.GetAsync("/api/models", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = JsonDocument.Parse(body).RootElement;
        Assert.Equal(Default, list.GetProperty("defaultRef").GetString());
        Assert.False(list.GetProperty("canWrite").GetBoolean());
        Assert.Equal(["kagent/azure", Default, "other-ns/elsewhere"], list.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("ref").GetString()));
        var byRef = list.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("ref").GetString()!);
        Assert.True(byRef[Default].GetProperty("isDefault").GetBoolean());
        Assert.True(byRef[Default].GetProperty("hasApiKey").GetBoolean());
        Assert.True(byRef[Default].GetProperty("accepted").GetBoolean());
        Assert.False(byRef["other-ns/elsewhere"].GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("https://az.example", byRef["kagent/azure"].GetProperty("params").GetProperty("azureEndpoint").GetString());
        Assert.Equal(["defaultHeaders", "azureOpenAI.azureAdToken"], byRef["kagent/azure"].GetProperty("kept").EnumerateArray().Select(k => k.GetString()));
        foreach (var secret in new[] { ApiKey, "AAD-TOKEN-SECRET", "HEADER-SECRET", "OPENAI_API_KEY" })
            Assert.DoesNotContain(secret, body);
        Assert.True((await _owner.GetFromJsonAsync<JsonElement>("/api/models", Ct)).GetProperty("canWrite").GetBoolean());
    }

    [Fact]
    public async Task Detail_IsOneModel_AndAnUnknownOneIs404()
    {
        _kagent.ModelConfigs["kagent/fast"] = new JsonObject
        {
            ["provider"] = "Anthropic", ["model"] = "claude-haiku-4-5", ["anthropic"] = new JsonObject { ["maxTokens"] = 2048, ["topK"] = 5 }
        };

        var detail = await _owner.GetFromJsonAsync<JsonElement>("/api/models/kagent/fast", Ct);

        Assert.Equal("Anthropic", detail.GetProperty("provider").GetString());
        Assert.Equal("2048", detail.GetProperty("params").GetProperty("maxTokens").GetString());
        Assert.False(detail.GetProperty("isDefault").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await _owner.GetAsync("/api/models/kagent/nope", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _owner.GetAsync("/api/models/Kagent/fast", Ct)).StatusCode);
    }

    // --- Create ---

    [Fact]
    public async Task Options_AreKagentsProviders_WithTheirFields_AndKagentsModels()
    {
        var options = await _owner.GetFromJsonAsync<JsonElement>("/api/models/options", Ct);

        Assert.Equal("kagent", options.GetProperty("namespace").GetString());
        var providers = options.GetProperty("providers").EnumerateArray().ToDictionary(p => p.GetProperty("type").GetString()!);
        // kagent's order; a provider Skanyxx does not know the fields of is not offered.
        Assert.Equal(["OpenAI", "Anthropic", "AzureOpenAI", "Ollama", "Gemini", "GeminiVertexAI", "AnthropicVertexAI", "Bedrock", "SAPAICore", "Foundry"], providers.Keys);
        Assert.Contains("gpt-4.1", providers["OpenAI"].GetProperty("models").EnumerateArray().Select(m => m.GetString()));
        Assert.False(providers["Ollama"].GetProperty("takesKey").GetBoolean());
        var azure = providers["AzureOpenAI"].GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!);
        Assert.True(azure["apiVersion"].GetProperty("required").GetBoolean());
        Assert.False(azure.ContainsKey("azureAdToken")); // an inline credential is never a form field
        var vertex = providers["GeminiVertexAI"].GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("key").GetString()).ToList();
        Assert.Contains("projectID", vertex);
        var bedrock = providers["Bedrock"].GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!);
        Assert.Equal("boolean", bedrock["promptCaching"].GetProperty("kind").GetString());
        Assert.Equal("integer", bedrock["readTimeout"].GetProperty("kind").GetString());
        Assert.False(bedrock.ContainsKey("guardrail"));
    }

    public static TheoryData<string, string, object, string?, bool, string> Providers() => new()
    {
        { "OpenAI", "gpt-4.1", new { baseUrl = "https://api.example/v1", maxTokens = "1024", organization = "" }, ApiKey, false,
            """{"provider":"OpenAI","model":"gpt-4.1","openAI":{"baseUrl":"https://api.example/v1","maxTokens":1024}}""" },
        { "Anthropic", "claude-sonnet-4-6", new { topK = "40", temperature = "0.2" }, ApiKey, false,
            """{"provider":"Anthropic","model":"claude-sonnet-4-6","anthropic":{"temperature":"0.2","topK":40}}""" },
        { "Ollama", "qwen3-coder:30b", new { host = "http://ollama:11434" }, null, false,
            """{"provider":"Ollama","model":"qwen3-coder:30b","ollama":{"host":"http://ollama:11434"}}""" },
        { "AzureOpenAI", "gpt-4.1", new { azureEndpoint = "https://az.example", apiVersion = "2024-10-21", azureDeployment = "prod" }, ApiKey, false,
            """{"provider":"AzureOpenAI","model":"gpt-4.1","azureOpenAI":{"azureEndpoint":"https://az.example","apiVersion":"2024-10-21","azureDeployment":"prod"}}""" },
        { "Gemini", "gemini-2.5-pro", new { maxOutputTokens = "8192" }, ApiKey, false,
            """{"provider":"Gemini","model":"gemini-2.5-pro","gemini":{"maxOutputTokens":8192}}""" },
        { "GeminiVertexAI", "gemini-2.5-pro", new { projectID = "acme", location = "us-central1" }, ApiKey, false,
            """{"provider":"GeminiVertexAI","model":"gemini-2.5-pro","geminiVertexAI":{"projectID":"acme","location":"us-central1"}}""" },
        { "AnthropicVertexAI", "claude-sonnet-4-6", new { projectID = "acme", location = "europe-west1", maxTokens = "4096" }, ApiKey, false,
            """{"provider":"AnthropicVertexAI","model":"claude-sonnet-4-6","anthropicVertexAI":{"projectID":"acme","location":"europe-west1","maxTokens":4096}}""" },
        { "Bedrock", "global.anthropic.claude-sonnet-4-6", new { region = "eu-west-1", promptCaching = "true" }, null, true,
            """{"provider":"Bedrock","model":"global.anthropic.claude-sonnet-4-6","bedrock":{"region":"eu-west-1","promptCaching":true}}""" },
        { "SAPAICore", "gpt-4o", new { baseUrl = "https://sap.example", resourceGroup = "default" }, ApiKey, false,
            """{"provider":"SAPAICore","model":"gpt-4o","sapAICore":{"baseUrl":"https://sap.example","resourceGroup":"default"}}""" },
        { "Foundry", "gpt-4.1", new { endpoint = "https://foundry.example", deployment = "gpt41" }, ApiKey, false,
            """{"provider":"Foundry","model":"gpt-4.1","foundry":{"endpoint":"https://foundry.example","deployment":"gpt41"}}""" }
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Create_PerProvider_SendsTheTypedSpec_AndTheKeyInline(string provider, string model, object fields, string? apiKey, bool noApiKey, string spec)
    {
        var response = await _owner.PostAsJsonAsync("/api/models",
            new { @namespace = "kagent", name = "team-model", provider, model, @params = fields, apiKey, noApiKey }, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var create = Assert.Single(_kagent.CallsTo("POST", "/api/modelconfigs"));
        Assert.Equal("kagent/team-model", create.Body!["ref"]!.GetValue<string>());
        Assert.Equal(spec, create.Body["spec"]!.ToJsonString());
        Assert.Equal(apiKey, create.Body["apiKey"]?.GetValue<string>());
        var created = JsonDocument.Parse(body).RootElement;
        Assert.Equal("kagent/team-model", created.GetProperty("ref").GetString());
        Assert.Equal(apiKey is not null, created.GetProperty("hasApiKey").GetBoolean());
        Assert.DoesNotContain(ApiKey, body);
        if (apiKey is not null)
            Assert.Equal(ApiKey, _kagent.ModelSecrets["kagent/team-model"][$"{provider.ToUpperInvariant()}_API_KEY"]);
    }

    [Theory]
    [InlineData("OpenAI", """{"tokenExchange":"x"}""", "sk-x", false, "not a OpenAI field this form sets")] // kagent knows it, the form does not: refused, not dropped
    [InlineData("OpenAI", """{"bogus":"1"}""", "sk-x", false, "not a OpenAI field")]
    [InlineData("OpenAI", """{"maxTokens":"many"}""", "sk-x", false, "must be a whole number")]
    [InlineData("OpenAI", """{"maxTokens":"-5"}""", "sk-x", false, "must be a whole number")]
    [InlineData("Bedrock", """{"region":"eu-west-1","promptCaching":"maybe"}""", null, true, "must be true or false")]
    [InlineData("AzureOpenAI", """{"azureEndpoint":"https://az.example"}""", "sk-x", false, "'apiVersion' is required")]
    [InlineData("Ollama", """{}""", null, false, "'host' is required")]
    [InlineData("Ollama", """{"host":"http://user:pw@ollama:11434"}""", null, false, "absolute http(s) URL")]
    [InlineData("OpenAI", """{"baseUrl":"file:///etc/passwd"}""", "sk-x", false, "absolute http(s) URL")]
    [InlineData("OpenAI", """{}""", null, false, "Paste the OpenAI API key")]
    [InlineData("Ollama", """{"host":"http://ollama:11434"}""", "sk-x", false, "Ollama takes no API key")]
    [InlineData("OpenAI", """{}""", "sk-x", true, "not both")]
    [InlineData("OpenAI", """{}""", "sk-LEAK x", false, "must not contain spaces")]
    [InlineData("FutureProvider", """{}""", "sk-x", false, "'Provider' must be one of")]
    public async Task Create_RefusesWhatKagentWouldDropOrRefuse_BeforeAnyWrite(string provider, string fields, string? apiKey, bool noApiKey, string problem)
    {
        var response = await _owner.PostAsJsonAsync("/api/models",
            new { @namespace = "kagent", name = "team-model", provider, model = "m-1", @params = JsonNode.Parse(fields), apiKey, noApiKey }, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem, body);
        if (apiKey is not null)
            Assert.DoesNotContain(apiKey, body);
        Assert.Empty(_kagent.CallsTo("POST", "/api/modelconfigs"));
    }

    [Theory]
    [InlineData("Kagent", "x")]
    [InlineData("kagent", "has_underscore")]
    [InlineData("kagent", "")]
    public async Task Create_NeedsKubernetesNames(string ns, string name)
    {
        var response = await _owner.PostAsJsonAsync("/api/models",
            new { @namespace = ns, name, provider = "Ollama", model = "llama3", @params = new { host = "http://o:11434" } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_kagent.CallsTo("POST", "/api/modelconfigs"));
    }

    [Fact]
    public async Task Create_ATakenName_Is409()
    {
        _kagent.ModelConfigs["kagent/taken"] = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3" };

        var response = await _owner.PostAsJsonAsync("/api/models",
            new { @namespace = "kagent", name = "taken", provider = "Ollama", model = "qwen3", @params = new { host = "http://o:11434" } }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("exists", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reason").GetString());
        Assert.Equal("llama3", _kagent.ModelConfigs["kagent/taken"]["model"]!.GetValue<string>());
    }

    // --- Edit ---

    [Fact]
    public async Task Edit_SameProvider_KeepsTheKeyAndEverythingTheFormDoesNotOwn()
    {
        _kagent.ModelConfigs["kagent/team"] = new JsonObject
        {
            ["provider"] = "OpenAI", ["model"] = "gpt-4o", ["apiKeySecret"] = "team", ["apiKeySecretKey"] = "OPENAI_API_KEY",
            ["openAI"] = new JsonObject
            {
                ["organization"] = "acme", ["temperature"] = "0.2",
                ["tokenExchange"] = new JsonObject { ["type"] = "GDCHServiceAccount", ["gdchServiceAccount"] = new JsonObject { ["audience"] = "a" } }
            },
            ["tls"] = new JsonObject { ["caCertSecretRef"] = "ca", ["caCertSecretKey"] = "ca.crt" }
        };

        var response = await _owner.PutAsJsonAsync("/api/models/kagent/team",
            new { provider = "OpenAI", model = "gpt-4.1", @params = new { organization = "", temperature = "0.5", maxTokens = "512" } }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var put = Assert.Single(_kagent.CallsTo("PUT", "/api/modelconfigs/kagent/team"));
        Assert.Null(put.Body!["apiKey"]);
        Assert.Equal("""{"provider":"OpenAI","model":"gpt-4.1","apiKeySecret":"team","apiKeySecretKey":"OPENAI_API_KEY","openAI":{"temperature":"0.5","tokenExchange":{"type":"GDCHServiceAccount","gdchServiceAccount":{"audience":"a"}},"maxTokens":512},"tls":{"caCertSecretRef":"ca","caCertSecretKey":"ca.crt"}}""",
            put.Body["spec"]!.ToJsonString());
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(["tls", "openAI.tokenExchange"], saved.GetProperty("kept").EnumerateArray().Select(k => k.GetString()));
    }

    [Fact]
    public async Task Edit_TheRouteNamesTheModel_WhateverTheBodySays()
    {
        _kagent.ModelConfigs["kagent/team"] = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3", ["ollama"] = new JsonObject { ["host"] = "http://o:11434" } };

        var response = await _owner.PutAsJsonAsync("/api/models/kagent/team",
            new { @namespace = "other-ns", name = "elsewhere", provider = "Ollama", model = "qwen3", @params = new { host = "http://o:11434" } }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("qwen3", _kagent.ModelConfigs["kagent/team"]["model"]!.GetValue<string>());
        Assert.Equal("gpt-4o", _kagent.ModelConfigs["other-ns/elsewhere"]["model"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await _owner.PutAsJsonAsync("/api/models/kagent/nope",
            new { provider = "Ollama", model = "qwen3", @params = new { host = "http://o:11434" } }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Edit_ProviderChange_StartsClean_AndAKeyedProviderNeedsAKey()
    {
        _kagent.ModelConfigs["kagent/team"] = new JsonObject
        {
            ["provider"] = "Ollama", ["model"] = "llama3", ["ollama"] = new JsonObject { ["host"] = "http://old:11434", ["options"] = new JsonObject { ["num_ctx"] = "32768" } }
        };

        var noKey = await _owner.PutAsJsonAsync("/api/models/kagent/team", new { provider = "Anthropic", model = "claude-sonnet-4-6" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, noKey.StatusCode);
        Assert.Contains("Paste the Anthropic API key", await noKey.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_kagent.CallsTo("PUT", "/api/modelconfigs/kagent/team"));

        var withKey = await _owner.PutAsJsonAsync("/api/models/kagent/team", new { provider = "Anthropic", model = "claude-sonnet-4-6", apiKey = ApiKey }, Ct);
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
        var put = Assert.Single(_kagent.CallsTo("PUT", "/api/modelconfigs/kagent/team"));
        Assert.Equal("""{"provider":"Anthropic","model":"claude-sonnet-4-6"}""", put.Body!["spec"]!.ToJsonString());
        Assert.Equal(ApiKey, _kagent.ModelSecrets["kagent/team"]["ANTHROPIC_API_KEY"]);
    }

    [Fact]
    public async Task Edit_ANewEndpointForTheStoredKey_NeedsTheKeyAgain()
    {
        _kagent.ModelConfigs["kagent/team"] = Keyed("OpenAI", "gpt-4o", "team");
        _kagent.ModelConfigs["kagent/team"]["openAI"] = new JsonObject { ["baseUrl"] = "https://api.openai.com/v1" };

        var moved = await _owner.PutAsJsonAsync("/api/models/kagent/team", new { provider = "OpenAI", model = "gpt-4.1", @params = new { baseUrl = "https://attacker.example/v1" } }, Ct);
        var dropped = await _owner.PutAsJsonAsync("/api/models/kagent/team", new { provider = "OpenAI", model = "gpt-4.1" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Contains("Paste the OpenAI API key again", await moved.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, dropped.StatusCode);
        Assert.Empty(_kagent.CallsTo("PUT", "/api/modelconfigs/kagent/team"));

        var same = await _owner.PutAsJsonAsync("/api/models/kagent/team", new { provider = "OpenAI", model = "gpt-4.1", @params = new { baseUrl = "https://api.openai.com/v1" } }, Ct);
        var movedWithKey = await _owner.PutAsJsonAsync("/api/models/kagent/team",
            new { provider = "OpenAI", model = "gpt-4.1", @params = new { baseUrl = "https://proxy.example/v1" }, apiKey = ApiKey }, Ct);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(HttpStatusCode.OK, movedWithKey.StatusCode);
        Assert.Equal("https://proxy.example/v1", _kagent.ModelConfigs["kagent/team"]["openAI"]!["baseUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task Edit_SwitchingKeyedProviders_LeavesNoStaleKeyInKagentsSecret()
    {
        _kagent.ModelConfigs["kagent/team"] = Keyed("OpenAI", "gpt-4o", "team");
        _kagent.ModelSecrets["kagent/team"] = new() { ["OPENAI_API_KEY"] = "sk-old-openai" };

        var response = await _owner.PutAsJsonAsync("/api/models/kagent/team", new { provider = "Anthropic", model = "claude-sonnet-4-6", apiKey = ApiKey }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = ApiKey }, _kagent.ModelSecrets["kagent/team"]);
        var puts = _kagent.CallsTo("PUT", "/api/modelconfigs/kagent/team").ToList();
        Assert.Equal(2, puts.Count);
        Assert.Null(puts[0].Body!["apiKey"]);
        Assert.Equal(ApiKey, puts[1].Body!["apiKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task Edit_NoApiKey_DropsTheKeyReference_AndKagentItsSecret()
    {
        _kagent.ModelConfigs["kagent/team"] = Keyed("OpenAI", "gpt-4o", "team");
        _kagent.ModelSecrets["kagent/team"] = new() { ["OPENAI_API_KEY"] = "sk-old-openai" };

        var response = await _owner.PutAsJsonAsync("/api/models/kagent/team",
            new { provider = "OpenAI", model = "gpt-4o", @params = new { baseUrl = "https://gateway.example/v1" }, noApiKey = true }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"provider":"OpenAI","model":"gpt-4o","openAI":{"baseUrl":"https://gateway.example/v1"}}""", _kagent.ModelConfigs["kagent/team"].ToJsonString());
        Assert.False(_kagent.ModelSecrets.ContainsKey("kagent/team"));
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("hasApiKey").GetBoolean());
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_AModelAgentsUse_ListsThemFirst_AndDeletesOnConfirm()
    {
        _kagent.ModelConfigs["kagent/team"] = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3" };
        _kagent.AgentObjects["kagent/helper"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "helper", ["namespace"] = "kagent" },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["modelConfig"] = "team", ["systemMessage"] = "x" } }
        };
        _kagent.AgentObjects["other-ns/team-user"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "team-user", ["namespace"] = "other-ns" },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["modelConfig"] = "team" } }
        };

        var asked = await _owner.DeleteAsync("/api/models/kagent/team", Ct);

        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode);
        var conflict = await asked.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("in_use", conflict.GetProperty("reason").GetString());
        // Same name in another namespace is another model: only kagent/helper uses kagent/team.
        Assert.Equal(["kagent/helper"], conflict.GetProperty("current").GetProperty("agents").EnumerateArray().Select(a => a.GetString()));
        Assert.Contains("kagent/helper", conflict.GetProperty("message").GetString());
        Assert.True(_kagent.ModelConfigs.ContainsKey("kagent/team"));
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/modelconfigs"));

        var confirmed = await _owner.DeleteAsync("/api/models/kagent/team?confirm=true", Ct);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.False(_kagent.ModelConfigs.ContainsKey("kagent/team"));
    }

    [Fact]
    public async Task Delete_AModelNoAgentUses_IsImmediate_AndItsKeyGoesWithIt()
    {
        _kagent.ModelConfigs["kagent/unused"] = Keyed("OpenAI", "gpt-4o", "unused");
        _kagent.ModelSecrets["kagent/unused"] = new() { ["OPENAI_API_KEY"] = "sk-x" };

        var response = await _owner.DeleteAsync("/api/models/kagent/unused", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(_kagent.ModelConfigs.ContainsKey("kagent/unused"));
        Assert.False(_kagent.ModelSecrets.ContainsKey("kagent/unused"));
        Assert.Equal(HttpStatusCode.NotFound, (await _owner.DeleteAsync("/api/models/kagent/unused", Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_TheDefault_IsRefused_EvenConfirmed()
    {
        _kagent.ModelSpec = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3" };

        foreach (var path in new[] { $"/api/models/{Default}", $"/api/models/{Default}?confirm=true" })
        {
            var response = await _owner.DeleteAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var conflict = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("default", conflict.GetProperty("reason").GetString());
            Assert.Contains("KAgent:ModelConfig", conflict.GetProperty("message").GetString());
        }
        Assert.NotNull(_kagent.ModelSpec);
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/modelconfigs"));
    }

    // --- Who may do what ---

    [Fact]
    public async Task OwnerWrites_SupervisorReads_EveryoneElseIsRefused_BeforeKagentIsWritten()
    {
        _kagent.ModelConfigs["kagent/team"] = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3" };
        var supervisor = (await PersonAsync("sue@skanyxx.example", "supervisor")).Api;
        var builder = (await PersonAsync("bo@skanyxx.example", "builder")).Api;
        var employee = (await PersonAsync("eve@skanyxx.example", "employee")).Api;
        var reads = new[] { ("GET", "/api/models"), ("GET", "/api/models/kagent/team") };
        var writes = new[] { ("GET", "/api/models/options"), ("POST", "/api/models"), ("PUT", "/api/models/kagent/team"), ("DELETE", "/api/models/kagent/team"),
            ("DELETE", "/api/models/kagent/team?confirm=true") };

        foreach (var (method, path) in reads.Concat(writes))
            Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().SendAsync(Request(method, path), Ct)).StatusCode);
        foreach (var (method, path) in reads)
            Assert.Equal(HttpStatusCode.OK, (await supervisor.SendAsync(Request(method, path), Ct)).StatusCode);
        foreach (var (method, path) in writes)
            Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.SendAsync(Request(method, path), Ct)).StatusCode);
        foreach (var client in new[] { builder, employee })
            foreach (var (method, path) in reads.Concat(writes))
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, path), Ct)).StatusCode);

        Assert.DoesNotContain(_kagent.Calls, c => c.Method is "POST" or "PUT" or "DELETE" && c.Path.StartsWith("/api/modelconfigs", StringComparison.Ordinal));
        Assert.DoesNotContain(_kagent.Calls, c => c.Path.StartsWith("/api/modelproviderconfigs", StringComparison.Ordinal));
        Assert.True(_kagent.ModelConfigs.ContainsKey("kagent/team"));
    }

    [Fact]
    public async Task ThePage_IsTheOwnersAndTheSupervisors_OnlyTheOwnerGetsTheForm()
    {
        var supervisor = (await PersonAsync("sue@skanyxx.example", "supervisor")).Browser;
        var builder = (await PersonAsync("bo@skanyxx.example", "builder")).Browser;
        var employee = (await PersonAsync("eve@skanyxx.example", "employee")).Browser;
        var owner = new Browser(_host);
        Assert.Equal(HttpStatusCode.Redirect, (await owner.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword })).StatusCode);

        var ownerPage = await (await owner.GetAsync("/Model")).Content.ReadAsStringAsync(Ct);
        var supervisorPage = await supervisor.GetAsync("/Model");
        var supervisorHtml = await supervisorPage.Content.ReadAsStringAsync(Ct);

        Assert.Contains("id=\"model-form\"", ownerPage);
        Assert.Contains("data-can-write=\"true\"", ownerPage);
        Assert.Equal(HttpStatusCode.OK, supervisorPage.StatusCode);
        Assert.Contains("data-can-write=\"false\"", supervisorHtml);
        Assert.DoesNotContain("id=\"model-form\"", supervisorHtml);
        Assert.DoesNotContain("name=\"ApiKey\"", supervisorHtml);
        Assert.Contains("href=\"/Model\"", supervisorHtml);
        foreach (var browser in new[] { builder, employee })
        {
            var denied = await browser.GetAsync("/Model");
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/AccessDenied", denied.Headers.Location?.OriginalString);
            Assert.DoesNotContain("href=\"/Model\"", await (await browser.GetAsync("/Chat")).Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public async Task ThePage_KeepsPasswordManagersOut_AndHasNoKeyInIt()
    {
        _kagent.ModelSpec = Keyed("OpenAI", "gpt-4o", "default-model-config");
        _kagent.Secret["OPENAI_API_KEY"] = ApiKey;
        var owner = new Browser(_host);
        await owner.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });

        var page = await (await owner.GetAsync("/Model")).Content.ReadAsStringAsync(Ct);

        var input = System.Text.RegularExpressions.Regex.Match(page, "<input name=\"ApiKey\"[^>]*>").Value;
        Assert.Contains("type=\"password\"", input);
        Assert.Contains("value=\"\"", input);
        Assert.Contains("autocomplete=\"new-password\"", input);
        Assert.Contains("data-1p-ignore", input);
        Assert.Contains("data-lpignore=\"true\"", input);
        Assert.Contains("data-form-type=\"other\"", input);
        Assert.DoesNotContain(ApiKey, page);
    }

    // --- Secrets ---

    [Fact]
    public async Task AnApiKey_IsNeverReturned_NorLogged_NorEchoedFromKagent()
    {
        var (log, bodies) = await CapturingConsoleAsync(async () =>
        {
            var answers = new List<HttpResponseMessage>
            {
                await _owner.PostAsJsonAsync("/api/models", new { @namespace = "kagent", name = "keyed", provider = "OpenAI", model = "gpt-4.1", apiKey = ApiKey }, Ct),
                await _owner.PutAsJsonAsync("/api/models/kagent/keyed", new { provider = "OpenAI", model = "gpt-4o", apiKey = ApiKey }, Ct),
                await _owner.GetAsync("/api/models", Ct),
                await _owner.GetAsync("/api/models/kagent/keyed", Ct),
                await _owner.PostAsJsonAsync("/api/models", new { @namespace = "kagent", name = "bad", provider = "OpenAI", model = "gpt 4", apiKey = ApiKey }, Ct)
            };
            // kagent failing with a body that names its internals and echoes the request: a fixed text, nothing of kagent's.
            _kagent.ModelWriteStatus = 500;
            answers.Add(await _owner.PutAsJsonAsync("/api/models/kagent/keyed", new { provider = "OpenAI", model = "gpt-4.1", apiKey = ApiKey }, Ct));
            answers.Add(await _owner.PostAsJsonAsync("/api/models", new { @namespace = "kagent", name = "other", provider = "OpenAI", model = "gpt-4.1", apiKey = ApiKey }, Ct));
            return answers;
        });

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway],
            bodies.Select(b => b.StatusCode));
        foreach (var answer in bodies)
        {
            var body = await answer.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain(ApiKey, body);
            Assert.DoesNotContain("SECRET-INTERNAL", body);
        }
        Assert.Contains("kagent/keyed", log);
        Assert.Contains("created", log);
        Assert.DoesNotContain(ApiKey, log);
        Assert.Equal(ApiKey, _kagent.ModelSecrets["kagent/keyed"]["OPENAI_API_KEY"]);
    }

    [Fact]
    public async Task FieldNames_ThatDifferOnlyBySpaces_Are400_NotAServerError()
    {
        var response = await _owner.PostAsync("/api/models", new StringContent(
            """{"namespace":"kagent","name":"dup","provider":"Ollama","model":"qwen3","params":{"host":"http://a:11434","host ":"http://b:11434"}}""",
            System.Text.Encoding.UTF8, "application/json"), Ct);
        var trimmed = await _owner.PostAsJsonAsync("/api/models",
            new { @namespace = "kagent", name = "spaced", provider = "Ollama", model = "qwen3", @params = new Dictionary<string, string> { [" host "] = "http://a:11434" } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Each field may be given once", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(_kagent.ModelConfigs.ContainsKey("kagent/dup"));
        Assert.Equal(HttpStatusCode.Created, trimmed.StatusCode);
        Assert.Equal("http://a:11434", _kagent.ModelConfigs["kagent/spaced"]["ollama"]!["host"]!.GetValue<string>());
    }

    // --- Audit (D325) ---

    [Fact]
    public async Task CreateEditAndDelete_EachLeaveAnIntentAndAnOutcomeRow_WithFlags_NeverTheKey()
    {
        var created = await _owner.PostAsJsonAsync("/api/models", new
        {
            @namespace = "kagent", name = "audited", provider = "OpenAI", model = "gpt-4.1", apiKey = ApiKey,
            @params = new { baseUrl = "https://llm.example/v1" }
        }, Ct);
        var edited = await _owner.PutAsJsonAsync("/api/models/kagent/audited", new
        {
            provider = "OpenAI", model = "gpt-4o", apiKey = ApiKey + "2", @params = new { baseUrl = "https://other.example:8443/v2" }
        }, Ct);
        var deleted = await _owner.DeleteAsync("/api/models/kagent/audited", Ct);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK], new[] { created.StatusCode, edited.StatusCode, deleted.StatusCode });
        foreach (var action in new[] { "model.create_requested", "model.created", "model.update_requested", "model.updated", "model.delete_requested", "model.deleted" })
        {
            var row = Assert.Single((await AuditAsync(action)).EnumerateArray());
            Assert.Equal("kagent/audited", row.GetProperty("targetId").GetString());
            var raw = row.GetRawText();
            Assert.DoesNotContain(ApiKey, raw);
            Assert.DoesNotContain("/v1", raw); // origins only, never a path
            Assert.DoesNotContain("/v2", raw);
        }
        var create = (await AuditAsync("model.created"))[0].GetProperty("details");
        Assert.Equal("OpenAI", create.GetProperty("provider").GetString());
        Assert.Equal("gpt-4.1", create.GetProperty("model").GetString());
        Assert.True(create.GetProperty("keyChanged").GetBoolean());
        Assert.False(create.GetProperty("keyRemoved").GetBoolean());
        Assert.Equal("https://llm.example", create.GetProperty("endpoints").GetProperty("baseUrl").GetString());
        var update = (await AuditAsync("model.updated"))[0].GetProperty("details");
        Assert.True(update.GetProperty("baseUrlChanged").GetBoolean());
        Assert.Equal("https://other.example:8443", update.GetProperty("endpoints").GetProperty("baseUrl").GetString());
        Assert.Equal(["baseUrl"], update.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));

        var changes = (await _owner.GetFromJsonAsync<JsonElement>("/api/identity/audit/analytics", Ct)).GetProperty("adminChanges").EnumerateArray()
            .ToDictionary(v => v.GetProperty("value").GetString()!, v => v.GetProperty("count").GetInt64());
        Assert.Equal(3, changes["models"]);
    }

    [Fact]
    public async Task AFailedWrite_LeavesItsIntentAndAFailureRow_AndARefusedFormNoRowAtAll()
    {
        _kagent.ModelWriteStatus = 500;

        var failed = await _owner.PostAsJsonAsync("/api/models", new { @namespace = "kagent", name = "down", provider = "OpenAI", model = "gpt-4.1", apiKey = ApiKey }, Ct);
        var invalid = await _owner.PostAsJsonAsync("/api/models", new { @namespace = "kagent", name = "bad", provider = "OpenAI", model = "gpt 4", apiKey = ApiKey }, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Single((await AuditAsync("model.create_requested")).EnumerateArray());
        var failure = Assert.Single((await AuditAsync("model.write_failed")).EnumerateArray());
        Assert.Equal("kagent/down", failure.GetProperty("targetId").GetString());
        Assert.DoesNotContain(ApiKey, failure.GetRawText());
        Assert.Empty((await AuditAsync("model.created")).EnumerateArray());
    }

    [Fact]
    public async Task AFailedDelete_LeavesItsIntentAndAFailureRow()
    {
        _kagent.ModelConfigs["kagent/doomed"] = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3" };
        _kagent.ModelWriteStatus = 500;

        var response = await _owner.DeleteAsync("/api/models/kagent/doomed", Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("kagent/doomed", Assert.Single((await AuditAsync("model.delete_requested")).EnumerateArray()).GetProperty("targetId").GetString());
        var failure = Assert.Single((await AuditAsync("model.write_failed")).EnumerateArray());
        Assert.Equal("delete", failure.GetProperty("details").GetProperty("reason").GetString());
        Assert.Empty((await AuditAsync("model.deleted")).EnumerateArray());
    }

    [Fact]
    public async Task ADeleteRefusedAsInUse_OrAsTheDefault_LeavesNoRow()
    {
        _kagent.ModelConfigs["kagent/team"] = new JsonObject { ["provider"] = "Ollama", ["model"] = "llama3" };
        _kagent.AgentObjects["kagent/helper"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "helper", ["namespace"] = "kagent" },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["modelConfig"] = "team" } }
        };

        var inUse = await _owner.DeleteAsync("/api/models/kagent/team", Ct);
        var theDefault = await _owner.DeleteAsync($"/api/models/{Default}?confirm=true", Ct);

        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, theDefault.StatusCode);
        foreach (var action in new[] { "model.delete_requested", "model.deleted", "model.write_failed" })
            Assert.Empty((await AuditAsync(action)).EnumerateArray());
    }

    private async Task<JsonElement> AuditAsync(string action) =>
        await _owner.GetFromJsonAsync<JsonElement>($"/api/identity/audit?action={action}", Ct);

    [Fact]
    public async Task Writes_AreOriginGuarded()
    {
        var request = Request("POST", "/api/models");
        request.Headers.Add("Origin", "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, (await _owner.SendAsync(request, Ct)).StatusCode);
        Assert.Empty(_kagent.CallsTo("POST", "/api/modelconfigs"));
    }

    private static JsonObject Keyed(string provider, string model, string secret) => new()
    {
        ["provider"] = provider, ["model"] = model, ["apiKeySecret"] = secret, ["apiKeySecretKey"] = $"{provider.ToUpperInvariant()}_API_KEY"
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), path)
    {
        Content = method is "POST" or "PUT"
            ? JsonContent.Create(new { @namespace = "kagent", name = "team", provider = "Ollama", model = "qwen3", @params = new { host = "http://o:11434" } })
            : null
    };

    private static async Task<(string Console, T Result)> CapturingConsoleAsync<T>(Func<Task<T>> action)
    {
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            var result = await action();
            return (console.ToString(), result);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private async Task<(HttpClient Api, Browser Browser)> PersonAsync(string email, string role)
    {
        var invite = await _owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } }, Ct);
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var browser = new Browser(_host);
        var accepted = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/identity/invites/accept")
        {
            Content = JsonContent.Create(new { token, password = Password, useCookie = true })
        });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email, password = Password }, Ct);
        signIn.EnsureSuccessStatusCode();
        var bearer = (await signIn.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tokens").GetProperty("accessToken").GetString()!;
        return (_host.Client(bearer), browser);
    }
}
