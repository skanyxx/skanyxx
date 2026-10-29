namespace Skanyxx.Host.Tests;

/// <summary>D390 (NEW already had it; OLD D247(3)): there is no CORS policy (the old "AllowAll" is gone), so no response ever allows another origin.</summary>
[Collection(HostCollection.Name)]
public sealed class CorsTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("GET", "/health")]
    [InlineData("GET", "/api/identity/status")]
    [InlineData("GET", "/Privacy")]
    [InlineData("OPTIONS", "/api/chat")]
    [InlineData("OPTIONS", "/health")]
    public async Task NoResponse_AllowsAnotherOrigin(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Origin", "https://elsewhere.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await fixture.Host.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
    }
}
