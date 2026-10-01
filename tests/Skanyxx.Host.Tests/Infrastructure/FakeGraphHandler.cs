using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// Microsoft's token endpoint and Graph's checkMemberGroups, faked at the HTTP layer: records every request (with its
/// body) and answers membership from <see cref="MemberOf"/>. Anything else is a 599, so a stray call shows.
/// </summary>
public sealed class FakeGraphHandler : HttpMessageHandler
{
    public const string AccessToken = "graph-app-token";

    public ConcurrentQueue<(HttpMethod Method, Uri Uri, string? Authorization, string Body)> Requests { get; } = new();

    public HashSet<string> MemberOf { get; } = [];

    public HttpStatusCode CheckStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>When set, checkMemberGroups answers 200 with this body as <c>application/json</c> (malformed on purpose).</summary>
    public string? CheckBody { get; set; }

    public IEnumerable<(HttpMethod Method, Uri Uri, string? Authorization, string Body)> TokenRequests =>
        Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/oauth2/v2.0/token"));

    public IEnumerable<string[]> CheckBatches => Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/checkMemberGroups"))
        .Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("groupIds").EnumerateArray().Select(e => e.GetString()!).ToArray());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Enqueue((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        var path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host == "login.microsoftonline.com" && path.EndsWith("/oauth2/v2.0/token"))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = AccessToken, token_type = "Bearer", expires_in = 3599 }) };
        if (request.RequestUri.Host == "graph.microsoft.com" && path.EndsWith("/checkMemberGroups"))
        {
            if (CheckStatus != HttpStatusCode.OK)
                return new HttpResponseMessage(CheckStatus);
            if (CheckBody is not null)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CheckBody, System.Text.Encoding.UTF8, "application/json") };
            var asked = JsonDocument.Parse(body).RootElement.GetProperty("groupIds").EnumerateArray().Select(e => e.GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = asked.Where(MemberOf.Contains).ToArray() }) };
        }
        return new HttpResponseMessage((HttpStatusCode)599);
    }
}
