using System.Net;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class JiraSourceTests
{
    private const string Token = "SECRET-TOKEN-123";
    private const string Jql = "project = SDB AND text ~ \"PRIVATE-JQL\"";

    private const string Issue = """
        {"key":"SDB-7","fields":{"summary":"Refund twice","issuetype":{"name":"Bug"},"status":{"name":"To Do"},
         "priority":{"name":"High"},"labels":["billing"],"assignee":{"displayName":"Dana Levi"},"updated":"2026-09-20T09:00:00.000+0000",
         "description":{"type":"doc","version":1,"content":[
           {"type":"paragraph","content":[{"type":"text","text":"Clicking "},{"type":"text","text":"refund","marks":[{"type":"strong"}]},{"type":"text","text":" twice."}]},
           {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"charges twice"}]}]}]}]}}}
        """;

    private static (JiraTicketSource Source, FakeHandler Http) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string token = Token)
    {
        var http = new FakeHandler(respond);
        var options = Options.Create(new TicketsOptions
        {
            Jira = new JiraOptions { Site = "example.atlassian.net", Email = "bot@example.com", ApiToken = token, Jql = Jql }
        });
        return (new JiraTicketSource(new HttpClient(http), options), http);
    }

    [Fact]
    public async Task Get_MapsTheIssue_AndFlattensRichText()
    {
        var (source, http) = Create(_ => FakeHandler.Json(Issue));

        var ticket = await source.GetAsync("SDB-7", CancellationToken.None);

        Assert.Equal("https://example.atlassian.net/rest/api/3/issue/SDB-7?fields=summary,description,issuetype,status,priority,labels,assignee,updated",
            http.Requests[0].RequestUri!.ToString());
        Assert.Equal(("Refund twice", "Bug", "To Do", "High", "Dana Levi"), (ticket.Title, ticket.Type, ticket.Status, ticket.Priority, ticket.Assignee));
        Assert.Equal("Clicking refund twice.\ncharges twice", ticket.Description);
        Assert.Equal("https://example.atlassian.net/browse/SDB-7", ticket.Url);
        Assert.Equal(["billing"], ticket.Labels);
    }

    [Fact]
    public async Task List_SendsTheJqlEscaped_AndCapsThePage()
    {
        var (source, http) = Create(_ => FakeHandler.Json($$"""{"issues":[{{Issue}}]}"""));

        var tickets = await source.ListAsync(500, CancellationToken.None);

        var uri = http.Requests[0].RequestUri!;
        Assert.Equal("/rest/api/3/search/jql", uri.AbsolutePath);
        Assert.Contains("maxResults=100", uri.Query);
        Assert.Contains(Uri.EscapeDataString(Jql), uri.OriginalString);
        Assert.Single(tickets);
    }

    [Fact]
    public async Task EveryRequest_IsAGet_WithBasicAuth_AndNoBody()
    {
        var (source, http) = Create(r => r.RequestUri!.AbsolutePath.Contains("search") ? FakeHandler.Json("""{"issues":[]}""") : FakeHandler.Json(Issue));

        await source.ListAsync(10, CancellationToken.None);
        await source.GetAsync("SDB-7", CancellationToken.None);

        Assert.All(http.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.All(http.Bodies, Assert.Null);
        Assert.All(http.Requests, r => Assert.Equal("Basic", r.Headers.Authorization!.Scheme));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Jira refused the credential (HTTP 401).")]
    [InlineData(HttpStatusCode.BadRequest, "Jira answered HTTP 400.")]
    [InlineData(HttpStatusCode.InternalServerError, "Jira answered HTTP 500.")]
    public async Task Errors_NeverEchoTheTokenTheJqlOrTheUpstreamBody(HttpStatusCode status, string expected)
    {
        var (source, _) = Create(_ => FakeHandler.Json($$"""{"errorMessages":["bad query: {{Jql}} {{Token}}"]}""", status));

        var ex = await Assert.ThrowsAsync<TicketSourceException>(() => source.ListAsync(10, CancellationToken.None));

        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public async Task Unreachable_SaysSoByTypeName_NotByUrl()
    {
        var (source, _) = Create(_ => throw new HttpRequestException($"connect failed https://example.atlassian.net/?jql={Jql}"));

        var ex = await Assert.ThrowsAsync<TicketSourceException>(() => source.ListAsync(10, CancellationToken.None));

        Assert.Equal("Jira is unreachable (HttpRequestException).", ex.Message);
    }

    [Fact]
    public async Task PlainHttpSite_IsRefused_BeforeTheCredentialIsSent()
    {
        var http = new FakeHandler(_ => FakeHandler.Json(Issue));
        var source = new JiraTicketSource(new HttpClient(http), Options.Create(new TicketsOptions
        {
            Jira = new JiraOptions { Site = "http://example.atlassian.net", Email = "bot@example.com", ApiToken = Token, Project = "SDB" }
        }));

        var ex = await Assert.ThrowsAsync<TicketSourceException>(() => source.GetAsync("SDB-7", CancellationToken.None));

        Assert.Equal("The Jira site must use https.", ex.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task NulInTicketText_IsRemoved()
    {
        var (source, _) = Create(_ => FakeHandler.Json(Issue.Replace("Refund twice", "Refund\\u0000twice")));

        var ticket = await source.GetAsync("SDB-7", CancellationToken.None);

        Assert.Equal("Refundtwice", ticket.Title);
    }

    [Theory]
    [InlineData("""{"key":"SDB-7"}""")]
    [InlineData("""{"key":7,"fields":{}}""")]
    [InlineData("""{"issues":{"not":"an array"}}""")]
    public async Task AnUnexpectedShape_IsASourceError_NotACrash(string json)
    {
        var (source, _) = Create(_ => FakeHandler.Json(json));

        var ex = await Assert.ThrowsAsync<TicketSourceException>(() =>
            json.Contains("issues") ? source.ListAsync(10, CancellationToken.None) : source.GetAsync("SDB-7", CancellationToken.None));

        Assert.Equal("Jira answered in an unexpected shape.", ex.Message);
    }

    [Fact]
    public async Task MissingIssue_IsNotFound()
    {
        var (source, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<TicketNotFoundException>(() => source.GetAsync("SDB-9", CancellationToken.None));
    }

    [Fact]
    public async Task NoCredential_FailsWithoutCallingJira()
    {
        var (source, http) = Create(_ => FakeHandler.Json(Issue), token: "");

        var ex = await Assert.ThrowsAsync<TicketSourceException>(() => source.GetAsync("SDB-7", CancellationToken.None));

        Assert.Equal("No Jira credential is configured.", ex.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void TheSourceHasNoWriteVerb()
    {
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src/Modules/Skanyxx.Module.Tickets/Sources/JiraTicketSource.cs"));

        Assert.DoesNotContain("HttpMethod.Post", code);
        Assert.DoesNotContain("HttpMethod.Put", code);
        Assert.DoesNotContain("HttpMethod.Delete", code);
        Assert.DoesNotContain("HttpMethod.Patch", code);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(code, @"new HttpRequestMessage\("));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Skanyxx.sln")))
            dir = dir.Parent;
        return dir.FullName;
    }
}
