using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skanyxx.Core.Infrastructure;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D521: behind the kagent gate, every request any client sends to kagent carries a fresh HS256 token naming the user the
/// caller named, and neither <c>?user_id=</c> nor <c>X-User-Id</c>; nothing changes without <c>KAgent:Auth:Mode=signed</c>
/// (a local kagent). The gate's side is <c>scripts/helm/gate-test.sh</c>.
/// </summary>
public sealed class KAgentTokenTests
{
    private const string Key = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKL";
    private const string KAgentUrl = "http://kagent-gate.skanyxx.svc:8084";

    [Fact]
    public void AToken_IsSignedWithTheKey_ForTheGate_AndShortLived()
    {
        var now = DateTimeOffset.UtcNow;
        var token = new KAgentToken(new KAgentAuthConfig { Mode = KAgentAuthConfig.Signed, TokenKey = Key, TokenSeconds = 45 }, new FixedTime(now))
            .Issue("ana@acme.example");

        var claims = Verify(token);
        Assert.Equal("skanyxx", claims.GetProperty("iss").GetString());
        Assert.Equal("kagent-gate", claims.GetProperty("aud").GetString());
        Assert.Equal("ana@acme.example", claims.GetProperty("sub").GetString());
        Assert.Equal(now.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.Equal(now.ToUnixTimeSeconds() + 45, claims.GetProperty("exp").GetInt64());
        Assert.Throws<CryptographicException>(() => Verify(token, "another-key-another-key-another-key-another"));
    }

    [Fact]
    public async Task EveryKAgentCall_CarriesATokenForItsUser_AndNoUserIdOfItsOwn()
    {
        var (client, kagent, _) = Build(Signed());

        await client.CreateSessionAsync("seed", "ana@acme.example", TestContext.Current.CancellationToken);
        await client.GetAgentsAsync(TestContext.Current.CancellationToken);

        var session = kagent.Requests[0];
        Assert.Equal("ana@acme.example", Verify(session.Bearer!).GetProperty("sub").GetString());
        Assert.DoesNotContain("user_id", session.Uri.Query);
        Assert.Null(session.UserIdHeader);
        // Skanyxx's own calls (D250) are its process user, as before; never a request header's.
        var agents = kagent.Requests[1];
        Assert.False(string.IsNullOrEmpty(Verify(agents.Bearer!).GetProperty("sub").GetString()));
        Assert.Equal("/api/agents", agents.Uri.AbsolutePath);
        Assert.Empty(agents.Uri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task AModulesOwnClient_IsSignedToo_FromItsXUserId()
    {
        var (_, kagent, http) = Build(Signed());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{KAgentUrl}/api/a2a/skanyxx/seed/?keep=1");
        request.Headers.Add("X-User-Id", "skanyxx-investigate");

        using var _ = await http.CreateClient("investigate").SendAsync(request, TestContext.Current.CancellationToken);

        var sent = Assert.Single(kagent.Requests);
        Assert.Equal("skanyxx-investigate", Verify(sent.Bearer!).GetProperty("sub").GetString());
        Assert.Null(sent.UserIdHeader);
        Assert.Equal("?keep=1", sent.Uri.Query);
    }

    [Fact]
    public async Task AnotherOrigin_GetsNoToken()
    {
        var (_, kagent, http) = Build(Signed());

        using var _ = await http.CreateClient("jira").GetAsync("https://acme.atlassian.net/rest/api/3/myself?user_id=x", TestContext.Current.CancellationToken);

        var sent = Assert.Single(kagent.Requests);
        Assert.Null(sent.Bearer);
        Assert.Contains("user_id=x", sent.Uri.Query);
    }

    [Fact]
    public async Task WithoutSignedMode_KAgentIsCalledAsBefore()
    {
        var (client, kagent, _) = Build(new Dictionary<string, string?> { ["KAgent:IngressUrl"] = KAgentUrl });

        await client.CreateSessionAsync("seed", "ana@acme.example", TestContext.Current.CancellationToken);

        var sent = Assert.Single(kagent.Requests);
        Assert.Null(sent.Bearer);
        Assert.Contains("user_id=ana%40acme.example", sent.Uri.Query);
        Assert.Equal("ana@acme.example", sent.UserIdHeader);
    }

    [Theory]
    [InlineData("signed", null, "60", "needs KAgent:Auth:TokenKey")]
    [InlineData("signed", "too-short", "60", "needs KAgent:Auth:TokenKey")]
    [InlineData("signed", Key, "301", "TokenSeconds must be 10-300")]
    [InlineData("bearer", Key, "60", "KAgent:Auth:Mode must be none or signed")]
    public void ASignedModeThatCannotWork_StopsStartup(string mode, string? key, string seconds, string error)
    {
        var settings = new Dictionary<string, string?> { ["KAgent:Auth:Mode"] = mode, ["KAgent:Auth:TokenKey"] = key, ["KAgent:Auth:TokenSeconds"] = seconds };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddLogging().AddKAgentHttpClient(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));

        Assert.Contains(error, ex.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Signed() => new()
    {
        ["KAgent:IngressUrl"] = KAgentUrl,
        ["KAgent:Auth:Mode"] = "signed",
        ["KAgent:Auth:TokenKey"] = Key
    };

    private static (KAgentApiClient Client, Recorder KAgent, IHttpClientFactory Http) Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var recorder = new Recorder();
        var services = new ServiceCollection().AddLogging();
        services.AddKAgentHttpClient(configuration);
        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => recorder));
        var provider = services.BuildServiceProvider();
        var http = provider.GetRequiredService<IHttpClientFactory>();
        return (new KAgentApiClient(http.CreateClient(KAgentApiClient.HttpClientName), configuration, NullLogger<KAgentApiClient>.Instance), recorder, http);
    }

    /// <summary>The claims of a token this key signed; throws when the signature does not match.</summary>
    private static JsonElement Verify(string token, string key = Key)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal("HS256", JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])).RootElement.GetProperty("alg").GetString());
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
        if (!CryptographicOperations.FixedTimeEquals(expected, Base64Url.DecodeFromChars(parts[2])))
            throw new CryptographicException("bad signature");
        return JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement;
    }

    private sealed record Sent(Uri Uri, string? Bearer, string? UserIdHeader);

    private sealed class Recorder : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add(new Sent(request.RequestUri!, request.Headers.Authorization?.Parameter,
                    request.Headers.TryGetValues("X-User-Id", out var users) ? users.Single() : null));
            var body = request.RequestUri!.AbsolutePath == "/api/agents" ? "[]" : """{"id":"s1","user_id":"ana@acme.example"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
