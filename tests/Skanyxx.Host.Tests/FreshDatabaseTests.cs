using System.Net.Http.Json;
using Npgsql;
using Skanyxx.Host.Tests.Infrastructure;

namespace Skanyxx.Host.Tests;

/// <summary>
/// A first start on an empty database. Data Protection's key-ring service (registered by the Host before any module)
/// reads keys from the identity database on start, so the identity schema must exist before any hosted service runs;
/// and the ring it writes there is what lets a session outlive a restart.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class FreshDatabaseTests(PostgresFixture fixture)
{
    [Fact]
    public async Task FirstStart_WritesTheKeyRing_AndASessionSurvivesARestart()
    {
        var connectionString = await fixture.NewDatabaseAsync();
        string cookie;
        await using (var first = await HostApp.StartAsync(connectionString))
        {
            (await first.BootstrapAsync()).EnsureSuccessStatusCode();
            var signIn = await first.Client().PostAsJsonAsync("/api/identity/sign-in",
                new { email = HostApp.OwnerEmail, password = HostApp.OwnerPassword, useCookie = true });
            signIn.EnsureSuccessStatusCode();
            cookie = signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("skanyxx.auth=")).Split(';')[0];
        }

        var keys = await KeyCountAsync(connectionString);
        await using var second = await HostApp.StartAsync(connectionString);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
        request.Headers.Add("Cookie", cookie);
        var me = await second.Client().SendAsync(request);

        Assert.True(keys >= 1);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    private static async Task<long> KeyCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM identity_data_protection_keys", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
