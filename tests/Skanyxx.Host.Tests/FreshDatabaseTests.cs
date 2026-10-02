using System.Net.Http.Json;
using Npgsql;
using Skanyxx.Host.Tests.Infrastructure;

namespace Skanyxx.Host.Tests;

/// <summary>
/// A first start on an empty database. Data Protection's key-ring service (registered by the Host before any module)
/// reads keys from the identity database on start, so the identity schema must exist before any hosted service runs;
/// and the ring it writes there is what lets a session outlive a restart. Neither start logs an Error: the migrators
/// create each history table before EF reads it.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class FreshDatabaseTests(PostgresFixture fixture)
{
    [Fact]
    public async Task FirstStart_WritesTheKeyRing_AndASessionSurvivesARestart()
    {
        var connectionString = await fixture.NewDatabaseAsync();
        var logs = Directory.CreateTempSubdirectory("skanyxx-fresh-db-").FullName;
        string cookie;
        await using (var first = await HostApp.StartAsync(connectionString, ErrorsTo(Path.Combine(logs, "first.json"))))
        {
            (await first.BootstrapAsync()).EnsureSuccessStatusCode();
            var signIn = await first.Client().PostAsJsonAsync("/api/identity/sign-in",
                new { email = HostApp.OwnerEmail, password = HostApp.OwnerPassword, useCookie = true });
            signIn.EnsureSuccessStatusCode();
            cookie = signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("skanyxx.auth=")).Split(';')[0];
        }

        var keys = await KeyCountAsync(connectionString);
        await using (var second = await HostApp.StartAsync(connectionString, ErrorsTo(Path.Combine(logs, "second.json"))))
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
            request.Headers.Add("Cookie", cookie);
            var me = await second.Client().SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        Assert.True(keys >= 1);
        // Read after each host stopped, which flushes its log.
        Assert.Empty(Directory.GetFiles(logs).SelectMany(File.ReadAllLines));
        Directory.Delete(logs, recursive: true);
    }

    /// <summary>A Serilog file sink, through the Host's own configuration, that only takes Error and above.</summary>
    private static Action<Dictionary<string, string>> ErrorsTo(string path) => settings =>
    {
        settings["Serilog:WriteTo:0:Name"] = "File";
        settings["Serilog:WriteTo:0:Args:path"] = path;
        settings["Serilog:WriteTo:0:Args:restrictedToMinimumLevel"] = "Error";
        settings["Serilog:WriteTo:0:Args:formatter"] = "Serilog.Formatting.Compact.RenderedCompactJsonFormatter, Serilog.Formatting.Compact";
    };

    private static async Task<long> KeyCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM identity_data_protection_keys", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
