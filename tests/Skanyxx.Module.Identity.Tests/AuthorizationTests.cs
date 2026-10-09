namespace Skanyxx.Module.Identity.Tests;

/// <summary>The fallback policy: a protected route answers an unauthenticated API caller with a 401 ProblemDetails.</summary>
public sealed class AuthorizationTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    [Theory]
    [InlineData(IdentityApp.ProbePath)]
    [InlineData("/api/identity/me")]
    public async Task Anonymous_Is401Problem_NotARedirect(string path)
    {
        var response = await App.Client().GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UserIdHeader_AloneAuthenticatesNothing()
    {
        await App.BootstrapAsync();
        var client = App.Client();
        client.DefaultRequestHeaders.Add("X-User-Id", "owner");

        var response = await client.GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GarbageBearer_Is401Problem()
    {
        var response = await App.Client(bearer: "not-a-token").GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Status_IsAnonymous()
    {
        var response = await App.Client().GetAsync("/api/identity/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
