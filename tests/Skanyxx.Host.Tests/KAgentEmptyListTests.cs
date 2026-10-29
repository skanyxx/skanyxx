using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D540: kagent 0.10.2 answers an empty list without <c>data</c> (<c>{"error":false,"message":"Successfully listed
/// agents"}</c>), as on a fresh Helm install before the seed or the owner's model exist. That is an empty list, not a
/// 502; any other shape still is.
/// </summary>
public sealed class KAgentEmptyListTests
{
    private const string Empty = """{"error":false,"message":"Successfully listed"}""";

    [Fact]
    public async Task EveryList_AnsweredWithoutData_IsEmpty()
    {
        var client = Client(Empty);

        Assert.Empty(await client.GetAgentsAsync(Ct));
        Assert.Empty(await client.GetAgentListAsync(Ct));
        Assert.Empty(await client.GetModelConfigsAsync(Ct));
        Assert.Empty(await client.GetModelConfigRefsAsync(Ct));
        Assert.Empty(await client.GetToolServerListAsync(Ct));
        Assert.Empty(await client.GetModelProvidersAsync(Ct));
    }

    [Theory]
    [InlineData("""{"error":false,"data":{"name":"x"}}""")]
    [InlineData("""{"error":false,"data":"x"}""")]
    public async Task AnotherShape_IsStillABadGateway(string body)
    {
        var client = Client(body);

        var agents = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAgentsAsync(Ct));
        var models = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetModelConfigsAsync(Ct));

        Assert.Equal(HttpStatusCode.BadGateway, agents.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, models.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KAgentApiClient Client(string body)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["KAgent:IngressUrl"] = "http://kagent.test" })
            .Build();
        return new KAgentApiClient(new HttpClient(new Fixed(body)), configuration, NullLogger<KAgentApiClient>.Instance);
    }

    private sealed class Fixed(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
