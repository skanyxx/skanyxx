using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Infrastructure;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The kagent client must never retry (D107): <c>message/send</c> and the ModelConfig writes are not idempotent.
/// </summary>
public sealed class KAgentHttpClientTests
{
    [Fact]
    public async Task TheKAgentClient_SendsAFailedPostOnce()
    {
        var server = new Recording(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var services = new ServiceCollection().AddLogging();
        services.AddKAgentHttpClient(new ConfigurationBuilder().Build());
        services.AddHttpClient(KAgentApiClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(KAgentApiClient.HttpClientName);

        using var response = await client.PostAsync("http://kagent.test/api/a2a/ns/agent/", new StringContent("{}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, server.Attempts);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    private sealed class Recording(HttpResponseMessage response) : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => _attempts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(response);
        }
    }
}
