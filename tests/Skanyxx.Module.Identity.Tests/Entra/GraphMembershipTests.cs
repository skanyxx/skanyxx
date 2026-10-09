using System.Web;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>Overage resolution against Graph: app-only token, only the asked ids, batches of at most 20 (Graph's limit).</summary>
public sealed class GraphMembershipTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string ClientId = "22222222-2222-2222-2222-222222222222";
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly FakeGraphHandler _handler = new();

    [Fact]
    public async Task FortyFiveGroups_AreAskedInBatchesOfTwenty_WithOneAppOnlyToken()
    {
        var groups = Enumerable.Range(1, 45).Select(i => $"00000000-0000-0000-0000-{i:D12}").ToList();
        _handler.MemberOf.UnionWith([groups[0], groups[21], groups[44]]);

        var member = await Graph().MemberOfAsync(Config(), Oid, groups, CancellationToken.None);

        Assert.Equal([groups[0], groups[21], groups[44]], member.Order());
        Assert.Equal([20, 20, 5], _handler.CheckBatches.Select(b => b.Length));
        Assert.Equal(groups, _handler.CheckBatches.SelectMany(b => b));
        var token = Assert.Single(_handler.TokenRequests);
        Assert.Equal($"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token", token.Uri.ToString());
        var form = HttpUtility.ParseQueryString(token.Body);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("the-secret", form["client_secret"]);
        Assert.Equal("https://graph.microsoft.com/.default", form["scope"]);
        Assert.All(_handler.Requests.Where(r => r.Uri.Host == "graph.microsoft.com"), r =>
        {
            Assert.Equal($"https://graph.microsoft.com/v1.0/users/{Oid}/checkMemberGroups", r.Uri.ToString());
            Assert.Equal($"Bearer {FakeGraphHandler.AccessToken}", r.Authorization);
        });
    }

    /// <summary>CR L2: an answer that is not Graph's JSON is a failed call (the sign-in says "try again"), not a crash.</summary>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    public async Task AnUnreadableAnswer_IsAnHttpRequestException(string body)
    {
        _handler.CheckBody = body;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Graph().MemberOfAsync(Config(), Oid, ["00000000-0000-0000-0000-000000000001"], CancellationToken.None));
    }

    [Fact]
    public async Task AnIdGraphReturnsWithoutBeingAsked_IsIgnored()
    {
        var handler = new AnswersEverything();

        var member = await new GraphMembership(new HttpClient(handler), new EntraEndpoints(), TimeProvider.System)
            .MemberOfAsync(Config(), Oid, ["00000000-0000-0000-0000-000000000001"], CancellationToken.None);

        Assert.Equal(["00000000-0000-0000-0000-000000000001"], member);
    }

    [Fact]
    public async Task AnUnknownUser_HasNoGroups()
    {
        _handler.MemberOf.Add("00000000-0000-0000-0000-000000000001");
        _handler.CheckStatus = HttpStatusCode.NotFound;

        var member = await Graph().MemberOfAsync(Config(), Oid, ["00000000-0000-0000-0000-000000000001"], CancellationToken.None);

        Assert.Empty(member);
    }

    [Fact]
    public async Task AGraphFailure_Throws()
    {
        _handler.CheckStatus = HttpStatusCode.Forbidden;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Graph().MemberOfAsync(Config(), Oid, ["00000000-0000-0000-0000-000000000001"], CancellationToken.None));
    }

    [Fact]
    public async Task NoMappedGroups_AsksNothing()
    {
        var member = await Graph().MemberOfAsync(Config(), Oid, [], CancellationToken.None);

        Assert.Empty(member);
        Assert.Empty(_handler.Requests);
    }

    private GraphMembership Graph() => new(new HttpClient(_handler), new EntraEndpoints(), TimeProvider.System);

    private static EntraConfig Config() => new(true, Tenant, ClientId, "the-secret", new Dictionary<string, EntraGroupMapDto>(), 1);

    /// <summary>A Graph that claims membership of more than it was asked about.</summary>
    private sealed class AnswersEverything : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(request.RequestUri!.AbsolutePath.EndsWith("/token")
                    ? new { access_token = "t" } as object
                    : new { value = new[] { "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-0000000000ee" } })
            });
    }
}
