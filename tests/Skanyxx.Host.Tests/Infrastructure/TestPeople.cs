using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>Invited people with a role, signed in through the API (bearer) or the browser (cookie), and audit rows read straight from the database.</summary>
public static class TestPeople
{
    public const string Password = "a long member passphrase";

    /// <summary>Invites <paramref name="email"/> with <paramref name="role"/> and accepts: a bearer client and the person's id.</summary>
    public static async Task<(HttpClient Api, string Id)> InviteAsync(HostApp host, HttpClient owner, string email, string role)
    {
        var invite = await owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        return (host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!), body.GetProperty("user").GetProperty("id").GetString()!);
    }

    public static async Task<Browser> SignInAsync(HostApp host, string email, string password = Password)
    {
        var browser = new Browser(host);
        var signedIn = await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        return browser;
    }

    /// <summary>The owner signed in through the browser (bootstrapped first through the API).</summary>
    public static async Task<Browser> OwnerBrowserAsync(HostApp host)
    {
        await host.OwnerAsync();
        return await SignInAsync(host, HostApp.OwnerEmail, HostApp.OwnerPassword);
    }

    /// <summary>The audit rows of one action, oldest first: target and details as JSON text.</summary>
    public static async Task<List<(string? Target, string Details)>> AuditAsync(string database, string action)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "TargetId", COALESCE("Details"::text, '') FROM identity_audit WHERE "Action" = @action ORDER BY "Id" """, connection);
        command.Parameters.AddWithValue("action", action);
        var rows = new List<(string?, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    public static async Task<string> HtmlAsync(this Browser browser, string path) =>
        await (await browser.GetAsync(path)).Content.ReadAsStringAsync();
}
