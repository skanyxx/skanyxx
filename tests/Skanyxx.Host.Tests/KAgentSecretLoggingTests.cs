using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests;

/// <summary>
/// A kagent call that carried a secret value (a tool server's typed header values, a model API key) never logs kagent's
/// answer: a refusal could echo what it was sent. Only the status and a fixed text; other calls keep kagent's text.
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

    [Fact]
    public async Task ModelKeyWrite_NeitherLogsNorThrowsKagentsText()
    {
        var (client, logs) = Client();

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => client.SaveModelConfigAsync(true, new JsonObject(), Secret, Ct));

        Assert.DoesNotContain(Secret, refused.Message);
        Assert.DoesNotContain("kagent says no", refused.Message);
        var line = Assert.Single(logs);
        Assert.DoesNotContain(Secret, line);
        Assert.Contains(KAgentApiClient.SecretCallRefused, line);
    }

    [Fact]
    public async Task ModelSpecWrite_WithoutAKey_KeepsKagentsMessageForTheOwner()
    {
        var (client, _) = Client();

        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => client.SaveModelConfigAsync(false, new JsonObject(), null, Ct));

        Assert.Contains("kagent says no", refused.Message);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (KAgentApiClient Client, ConcurrentQueue<string> Logs) Client()
    {
        var logs = new ConcurrentQueue<string>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["KAgent:IngressUrl"] = "http://kagent.test" })
            .Build();
        var client = new KAgentApiClient(new HttpClient(new EchoRefusal()), configuration, new ListLogger(logs));
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

    private sealed class ListLogger(ConcurrentQueue<string> lines) : ILogger<KAgentApiClient>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception.Message));
    }
}
