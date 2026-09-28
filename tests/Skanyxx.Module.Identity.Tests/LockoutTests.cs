using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Module.Identity.Tests;

public sealed class LockoutTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
    }

    [Fact]
    public async Task FiveFailures_LockTheAccount_EvenForTheRightPassword_WithTheSameAnswer()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await App.SignInAsync(password: "wrong password " + i)).StatusCode);

        var locked = await App.SignInAsync();
        var body = await locked.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("Invalid email or password.", body.GetProperty("detail").GetString());
        await using var db = Postgres.CreateDbContext();
        Assert.True(db.Users.Single().LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task FourFailures_ThenTheRightPassword_SignsIn_AndResetsTheCount()
    {
        for (var i = 0; i < 4; i++)
            await App.SignInAsync(password: "wrong password " + i);

        var response = await App.SignInAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(0, db.Users.Single().AccessFailedCount);
    }
}
