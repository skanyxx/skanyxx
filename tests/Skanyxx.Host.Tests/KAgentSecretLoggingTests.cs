using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests;

/// <summary>
/// A kagent call that carried a secret value (a tool server's typed header values, a model API key on create or update)
/// never logs kagent's answer: a refusal could echo what it was sent. Only the status and a fixed text; other calls keep kagent's text.
/// </summary>
public sealed class KAgentSecretLoggingTests
{
    private const string Secret = "sk-header-value-0123456789abcdef";

    [Fact]
    public async Task ToolServerCreate_WithSecretValues_LogsOnlyTheStatus()
    {
        var (client, logs) = Client();

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CreateRemoteMcpServerAsync(new JsonObject { ["metadata"] = new JsonObject() }, [("srv-headers", "Authorization", Secret)], Ct));

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.DoesNotContain(Secret, refused.Message);
        var line = Assert.Single(logs);
        Assert.DoesNotContain(Secret, line);
        Assert.Contains("500", line);
        Assert.Contains(KAgentApiClient.SecretCallRefused, line);
    }

    [Fact]
    public async Task ToolServerCreate_WithoutSecrets_StillLogsKagentsText()
    {
        var (client, logs) = Client();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CreateRemoteMcpServerAsync(new JsonObject { ["metadata"] = new JsonObject() }, [], Ct));

        Assert.Contains("kagent says no", Assert.Single(logs));
    }

    public static TheoryData<string> KeyedModelWrites() => ["create", "update"];

    [Theory]
    [MemberData(nameof(KeyedModelWrites))]
    public async Task ModelWrite_WithAKey_NeitherLogsNorThrowsKagentsText(string write)
    {
        var (client, logs) = Client();

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => write == "create"
            ? client.CreateModelConfigAsync("kagent", "gpt", new JsonObject(), Secret, Ct)
            : client.UpdateModelConfigAsync("kagent", "gpt", new JsonObject(), Secret, Ct));

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.DoesNotContain(Secret, refused.Message);
        Assert.DoesNotContain("kagent says no", refused.Message);
        var line = Assert.Single(logs);
        Assert.DoesNotContain(Secret, line);
        Assert.DoesNotContain("kagent says no", line);
        Assert.Contains("/api/modelconfigs", line);
        Assert.Contains(KAgentApiClient.SecretCallRefused, line);
    }

    [Fact]
    public async Task ModelWrites_WithoutAKey_AndDelete_StillLogKagentsText()
    {
        var (client, logs) = Client();

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateModelConfigAsync("kagent", "ollama", new JsonObject(), null, Ct));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.UpdateModelConfigAsync("kagent", "ollama", new JsonObject(), "", Ct));
        var deleted = await Assert.ThrowsAsync<HttpRequestException>(() => client.DeleteModelConfigAsync("kagent", "ollama", Ct));

        Assert.Equal(3, logs.Count);
        Assert.All(logs, line => Assert.Contains("kagent says no", line));
        Assert.DoesNotContain("kagent says no", deleted.Message);
    }

    public static TheoryData<string> AgentWrites() => ["create", "update"];

    /// <summary>An edit, suspend or resume sends the whole Agent back; one made outside Skanyxx may hold an inline value (D327).</summary>
    [Theory]
    [MemberData(nameof(AgentWrites))]
    public async Task AgentWrite_WithAnInlineValue_LogsOnlyTheStatus(string write)
    {
        var (client, logs) = Client();
        var agent = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "byo", ["namespace"] = "kagent" },
            ["spec"] = new JsonObject
            {
                ["declarative"] = new JsonObject
                {
                    ["tools"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "McpServer",
                        ["headersFrom"] = new JsonArray(new JsonObject { ["name"] = "Authorization", ["value"] = Secret })
                    }),
                    ["deployment"] = new JsonObject { ["env"] = new JsonArray(new JsonObject { ["name"] = "TOKEN", ["value"] = Secret }) }
                }
            }
        };

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() =>
            write == "create" ? client.CreateAgentAsync(agent, Ct) : client.UpdateAgentAsync(agent, Ct));

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.DoesNotContain(Secret, refused.Message);
        var line = Assert.Single(logs);
        Assert.DoesNotContain(Secret, line);
        Assert.DoesNotContain("kagent says no", line);
        Assert.Contains("/api/agents", line);
        Assert.Contains(KAgentApiClient.SecretCallRefused, line);
    }

    /// <summary>
    /// D391: a call that carried no secret still logs kagent's text, but with every credential in it blanked — an error
    /// that echoes a spec can carry a header value, an API key or a token.
    /// </summary>
    [Fact]
    public async Task ALoggedRefusal_HasItsCredentialsBlanked()
    {
        const string key = "sk-" + "ant-api03-abcdefghijklmnopqrstuvwxyz0123456789";
        const string header = "abcd1234-secret-header-value";
        // As kagent (Go) writes it: quotes inside the message escaped as \", not \u0022.
        const string echo =
            $$"""{"error":true,"message":"invalid spec: headers [X-Api-Key: {{header}}] apiKey={{key}} \"token\": \"{{header}}\" Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123"}""";
        var (client, logs) = Client(new Fixed(echo));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CreateRemoteMcpServerAsync(new JsonObject { ["metadata"] = new JsonObject() }, [], Ct));

        var line = Assert.Single(logs);
        Assert.Contains("invalid spec", line);
        Assert.DoesNotContain(key, line);
        Assert.DoesNotContain(header, line);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123", line);
        Assert.Contains(SecretScan.Redacted, line);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (KAgentApiClient Client, ConcurrentQueue<string> Logs) Client(HttpMessageHandler? kagent = null)
    {
        var logs = new ConcurrentQueue<string>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["KAgent:IngressUrl"] = "http://kagent.test" })
            .Build();
        var client = new KAgentApiClient(new HttpClient(kagent ?? new EchoRefusal()), configuration, new ListLogger(logs));
        return (client, logs);
    }

    /// <summary>kagent refusing with a body that echoes the whole request, the worst case for a log line.</summary>
    private sealed class EchoRefusal : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var body = new JsonObject { ["error"] = true, ["message"] = $"kagent says no; it was sent {sent}" };
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                RequestMessage = request,
                Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class Fixed(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = request,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class ListLogger(ConcurrentQueue<string> lines) : ILogger<KAgentApiClient>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception.Message));
    }
}
