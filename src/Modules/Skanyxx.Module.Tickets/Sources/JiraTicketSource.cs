using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Sources;

/// <summary>
/// Jira Cloud, read-only: <see cref="GetJsonAsync"/> is the only transport call and it only GETs. No error
/// message carries the token, the JQL (Jira echoes it back) or the upstream body.
/// </summary>
public sealed class JiraTicketSource(HttpClient http, IOptions<TicketsOptions> options) : ITicketSource
{
    private const string Fields = "summary,description,issuetype,status,priority,labels,assignee,updated";

    private JiraOptions Jira => options.Value.Jira;

    public async Task<IReadOnlyList<Ticket>> ListAsync(int limit, CancellationToken ct)
    {
        using var body = await GetJsonAsync(
            $"/rest/api/3/search/jql?jql={Uri.EscapeDataString(Jql())}&maxResults={Math.Clamp(limit, 1, 100)}&fields={Fields}", ct);
        return Mapped(() => body.RootElement.TryGetProperty("issues", out var issues)
            ? issues.EnumerateArray().Select(ToTicket).ToList()
            : []);
    }

    public async Task<Ticket> GetAsync(string key, CancellationToken ct)
    {
        using var body = await GetJsonAsync($"/rest/api/3/issue/{key}?fields={Fields}", ct, notFoundKey: key);
        return Mapped(() => ToTicket(body.RootElement));
    }

    /// <summary>A body of an unexpected shape (or with invalid text) is Jira's problem: a 502, not a 500.</summary>
    private static T Mapped<T>(Func<T> map)
    {
        try
        {
            return map();
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new TicketSourceException("Jira answered in an unexpected shape.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken ct, string? notFoundKey = null)
    {
        if (!Jira.CredentialConfigured)
            throw new TicketSourceException("No Jira credential is configured.");

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl() + pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Jira.Email}:{Jira.ApiToken}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Type name only: the exception text carries the URL, and the URL carries the JQL.
            throw new TicketSourceException($"Jira is unreachable ({ex.GetType().Name}).");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound && notFoundKey is not null)
                throw new TicketNotFoundException(notFoundKey);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new TicketSourceException($"Jira refused the credential (HTTP {(int)response.StatusCode}).");
            if (!response.IsSuccessStatusCode)
                throw new TicketSourceException($"Jira answered HTTP {(int)response.StatusCode}.");
            try
            {
                return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            }
            catch (JsonException)
            {
                throw new TicketSourceException("Jira answered unparseable JSON.");
            }
        }
    }

    private string BaseUrl()
    {
        var site = Jira.Site.Trim().TrimEnd('/');
        if (site.Length == 0)
            throw new TicketSourceException("No Jira site is configured.");
        // The API token travels as Basic auth on every call: never over plain http.
        if (site.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new TicketSourceException("The Jira site must use https.");
        return site.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? site : $"https://{site}";
    }

    private string Jql()
    {
        if (Jira.Jql.Trim().Length > 0)
            return Jira.Jql.Trim();
        if (Jira.Project.Trim().Length == 0)
            throw new TicketSourceException("No Jira project or JQL is configured.");
        return $"project = {Jira.Project.Trim()} ORDER BY updated DESC";
    }

    private Ticket ToTicket(JsonElement issue)
    {
        var key = issue.GetProperty("key").GetString()!;
        var fields = issue.GetProperty("fields");
        return new Ticket(
            key,
            Text(fields, "summary"),
            fields.TryGetProperty("description", out var d) ? AdfText.From(d).Replace("\0", "") : "",
            Name(fields, "issuetype"),
            Name(fields, "status"),
            Name(fields, "priority"),
            fields.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
                ? labels.EnumerateArray().Select(l => (l.GetString() ?? "").Replace("\0", "")).ToList()
                : [],
            fields.TryGetProperty("assignee", out var a) && a.ValueKind == JsonValueKind.Object ? Text(a, "displayName") is { Length: > 0 } n ? n : null : null,
            $"{BaseUrl()}/browse/{key}",
            fields.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null);
    }

    /// <summary>NUL removed: Postgres text cannot store it, and a ticket snapshot must always save.</summary>
    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Replace("\0", "") : "";

    private static string Name(JsonElement fields, string property) =>
        fields.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Object ? Text(v, "name") : "";
}
