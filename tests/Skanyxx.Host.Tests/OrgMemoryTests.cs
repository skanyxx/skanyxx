using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D090 on the real host: identity's org tree decides memory's team scopes, across two separately loaded modules that
/// share only Core's <c>IOrgMembership</c>. A member writes, a non-member is refused, a supervisor reads and lifts to
/// company, and removing the member cuts their access on the next request.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class OrgMemoryTests(PostgresFixture fixture)
{
    private const string Password = "a long member passphrase";

    [Fact]
    public async Task TeamScopes_FollowTheOrgTree_AndARemovalCutsAccessAtOnce()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        var owner = await host.OwnerAsync();
        var (member, memberId) = await PersonAsync(host, owner, "mia@skanyxx.example", "employee");
        var (outsider, _) = await PersonAsync(host, owner, "otto@skanyxx.example", "employee");
        var (supervisor, _) = await PersonAsync(host, owner, "sam@skanyxx.example", "supervisor");
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync($"/api/identity/org/teams/billing/members/{memberId}", null, TestContext.Current.CancellationToken)).StatusCode);

        var write = await member.PutAsJsonAsync("/api/memory/cards/team:billing/refund-window",
            new { version = 0, type = "decision", what = "Refunds within 14 days", why = "Finance policy" }, cancellationToken: TestContext.Current.CancellationToken);
        var outsiderRead = await outsider.GetAsync("/api/memory/cards/team:billing/refund-window", TestContext.Current.CancellationToken);
        var outsiderSearch = await outsider.GetFromJsonAsync<JsonElement>("/api/memory/cards?q=refunds", cancellationToken: TestContext.Current.CancellationToken);
        var supervisorRead = await supervisor.GetAsync("/api/memory/cards/team:billing/refund-window", TestContext.Current.CancellationToken);
        var supervisorWrite = await supervisor.PutAsJsonAsync("/api/memory/cards/team:billing/other",
            new { version = 0, type = "fact", what = "w", why = "y" }, cancellationToken: TestContext.Current.CancellationToken);
        var lift = await supervisor.PostAsJsonAsync("/api/memory/cards/team:billing/refund-window/lift", new { targetScope = "company" }, cancellationToken: TestContext.Current.CancellationToken);
        var memberBefore = await member.GetAsync("/api/memory/cards/team:billing/refund-window", TestContext.Current.CancellationToken);

        var removed = await owner.DeleteAsync($"/api/identity/org/teams/billing/members/{memberId}", TestContext.Current.CancellationToken);
        var memberAfter = await member.GetAsync("/api/memory/cards/team:billing/refund-window", TestContext.Current.CancellationToken);
        var memberWriteAfter = await member.PutAsJsonAsync("/api/memory/cards/team:billing/refund-window",
            new { version = 1, type = "decision", what = "Changed", why = "y" }, cancellationToken: TestContext.Current.CancellationToken);
        var companyCopy = await member.GetAsync("/api/memory/cards/company/refund-window", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderRead.StatusCode);
        Assert.Equal(0, outsiderSearch.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, supervisorRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, supervisorWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Created, lift.StatusCode);
        Assert.Equal(HttpStatusCode.OK, memberBefore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberAfter.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberWriteAfter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, companyCopy.StatusCode); // the lifted copy is company knowledge now
    }

    /// <summary>
    /// A team slug with no org object behind it (a card from before the tree, or a typo): nobody is a member, so only
    /// oversight finds it. Accepted in D091 (SEC L1): such cards surface to the real team once the owner creates it.
    /// </summary>
    [Fact]
    public async Task ACardUnderAnOrphanTeamSlug_IsFoundByASupervisorsSearchOnly()
    {
        var database = await fixture.NewDatabaseAsync();
        await using var host = await HostApp.StartAsync(database);
        var owner = await host.OwnerAsync();
        var (employee, _) = await PersonAsync(host, owner, "emma@skanyxx.example", "employee");
        var (supervisor, _) = await PersonAsync(host, owner, "sam@skanyxx.example", "supervisor");
        await using (var connection = new NpgsqlConnection(database))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO memory_cards (scope, key, version, type, what, why, who, updated_at, status)
                VALUES ('team:ghost', 'orphan-refunds', 1, 'decision', 'Refunds within 30 days', 'Old policy', 'legacy', now(), 'published')
                """, connection);
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var supervisorHits = await supervisor.GetFromJsonAsync<JsonElement>("/api/memory/cards?q=refunds", cancellationToken: TestContext.Current.CancellationToken);
        var employeeHits = await employee.GetFromJsonAsync<JsonElement>("/api/memory/cards?q=refunds", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["team:ghost"], supervisorHits.EnumerateArray().Select(h => h.GetProperty("scope").GetString()));
        Assert.Equal(0, employeeHits.GetArrayLength());
    }

    private static async Task<(HttpClient Client, string Id)> PersonAsync(HostApp host, HttpClient owner, string email, string role)
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
}
