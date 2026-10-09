using System.Diagnostics;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>SEC M2: /health is bounded in time, says nothing but statuses, is not readable cross-origin and logs once.</summary>
[Collection(HostCollection.Name)]
public sealed class HealthTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Health_IsNotReadableCrossOrigin()
    {
        var health = await GetWithOriginAsync("/health");
        var control = await GetWithOriginAsync("/Privacy"); // anonymous legacy page, still under the global AllowAll policy

        Assert.True(control.Headers.Contains("Access-Control-Allow-Origin"), "control should get CORS headers");
        Assert.False(health.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task StalledDatabase_TimesOut_ReportsStatusOnly_LogsOneWarning()
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s => s["Skanyxx:HealthCheckTimeoutSeconds"] = "1");
        var client = host.Client();
        HttpResponseMessage response = null!;
        var console = new StringWriter();
        var original = Console.Out;
        var watch = Stopwatch.StartNew();

        await fixture.WhilePausedAsync(async () =>
        {
            Console.SetOut(console);
            try
            {
                response = await client.GetAsync("/health");
            }
            finally
            {
                Console.SetOut(original);
            }
        });
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var log = console.ToString();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("entries").GetProperty("memory-postgres").GetString());
        Assert.Equal(["status", "entries"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("description", body, StringComparison.OrdinalIgnoreCase);
        Assert.Single(log.Split('\n'), line => line.Contains("\"@l\":\"Warning\"") && line.Contains("Health "));
        Assert.DoesNotContain("\"@x\"", log); // no exception (stack trace) logged for the call
        Assert.DoesNotContain("Microsoft.Extensions.Diagnostics.HealthChecks", log); // its per-check Error lines are silenced
    }

    private Task<HttpResponseMessage> GetWithOriginAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", "https://evil.example");
        return fixture.Host.Client().SendAsync(request);
    }
}
