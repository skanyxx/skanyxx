namespace Skanyxx.Module.Identity.Tests;

/// <summary>CR m1 / SEC S9: null fields and control characters are a 400, never a NullReferenceException or a Postgres 22021.</summary>
public sealed class InputTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"email":null,"password":"correct horse battery"}""")]
    [InlineData("""{"email":"owner@skanyxx.example","password":null}""")]
    [InlineData("""{"email":"owner\u0000@skanyxx.example","password":"correct horse battery"}""")]
    [InlineData("""{"email":"owner\t@skanyxx.example","password":"correct horse battery"}""")]
    [InlineData("""{"email":"owner@skanyxx.example","password":"correct horse\u0000battery"}""")]
    public async Task SignIn_Is400(string json)
    {
        await App.BootstrapAsync();

        var response = await PostAsync("/api/identity/sign-in", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>SEC3 R3-5: the break-glass token is hashed per attempt, so its length is bounded like unlock's.</summary>
    [Fact]
    public async Task SignIn_WithABootstrapTokenOver512Characters_Is400()
    {
        await App.BootstrapAsync();

        var response = await App.SignInAsync(bootstrapToken: new string('x', 513));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"email":null,"password":"correct horse battery"}""")]
    [InlineData("""{"email":"owner@skanyxx.example","password":null}""")]
    [InlineData("""{"email":"owner\u0000@skanyxx.example","password":"correct horse battery"}""")]
    [InlineData("""{"email":"owner@skanyxx.example","password":"correct horse\u0007battery"}""")]
    public async Task Bootstrap_Is400_AndCreatesNobody(string json)
    {
        var response = await PostAsync("/api/identity/bootstrap", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await Postgres.UserCountAsync());
    }

    private Task<HttpResponseMessage> PostAsync(string path, string json) =>
        App.Client().PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
}
