using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// <c>POST /users/{oid}/checkMemberGroups</c> with an app-only token (client credentials, the sign-in's own client id
/// and secret; needs the application permission <c>GroupMember.Read.All</c> with admin consent). Asks only about the
/// mapped group ids, at most <see cref="BatchSize"/> per call (Graph's limit), and keeps only ids it asked about. One
/// token per sign-in: overage is rare and a sign-in is a handful of calls. Failures throw <see cref="HttpRequestException"/>,
/// an unreadable body included (CR L2), so the sign-in answers "try again" rather than a 500.
/// </summary>
internal sealed class GraphMembership(HttpClient http, EntraEndpoints endpoints) : IGraphMembership
{
    public const int BatchSize = 20;

    public async Task<IReadOnlySet<string>> MemberOfAsync(EntraConfig config, Guid objectId, IReadOnlyCollection<string> groupIds, CancellationToken ct)
    {
        var member = new HashSet<string>();
        if (groupIds.Count == 0)
            return member;

        var token = await TokenAsync(config, ct);
        foreach (var batch in groupIds.Chunk(BatchSize))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.CheckMemberGroups(objectId.ToString("D")))
            {
                Content = JsonContent.Create(new { groupIds = batch })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            // A deleted or unknown user: no groups, so no access.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new HashSet<string>();
            response.EnsureSuccessStatusCode();

            var asked = batch.ToHashSet();
            var body = await ReadAsync<GraphValues>(response, ct);
            member.UnionWith((body?.Value ?? []).Select(EntraClaims.NormalizeId).OfType<string>().Where(asked.Contains));
        }
        return member;
    }

    private async Task<string> TokenAsync(EntraConfig config, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret!,
            ["scope"] = EntraEndpoints.GraphScope
        });
        using var response = await http.PostAsync(endpoints.Token(config.TenantId), form, ct);
        response.EnsureSuccessStatusCode();
        return (await ReadAsync<TokenResponse>(response, ct))?.AccessToken
            ?? throw new HttpRequestException("The token endpoint answered without an access token.");
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new HttpRequestException($"{response.RequestMessage?.RequestUri?.Host} answered with a body that is not the expected JSON.", ex);
        }
    }

    private sealed record GraphValues(List<string>? Value);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);
}
