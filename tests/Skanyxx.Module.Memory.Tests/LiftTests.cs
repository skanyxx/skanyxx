using System.Net;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class LiftTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task Lift_CopiesUp_AndKeepsOriginal()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window", body: "details", source: "chat://42");

        var lifted = await ana.LiftAsync("personal:ana", "refund-window", "team:billing");

        Assert.Equal(HttpStatusCode.Created, lifted.StatusCode);
        var copy = await lifted.CardAsync();
        Assert.Equal("team:billing", copy.Scope);
        Assert.Equal(1, copy.Version);
        Assert.Equal("details", copy.Body);
        Assert.Equal("chat://42", copy.Source);

        await using var db = Postgres.CreateDbContext();
        var original = Assert.Single(db.Cards, c => c.Scope == "personal:ana");
        Assert.Equal(original.Id, copy.LiftedFromId);
        Assert.Equal(1, original.Version);
        Assert.Equal(2, db.Cards.Count());
    }

    [Fact]
    public async Task Lift_ToExistingTarget_Conflicts()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");
        await ana.LiftAsync("personal:ana", "refund-window", "team:billing");

        var again = await ana.LiftAsync("personal:ana", "refund-window", "team:billing");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("team:billing", "personal:ana")]
    [InlineData("company", "department:ops")]
    [InlineData("team:billing", "team:sales")]
    public async Task Lift_NotUpward_Rejected(string from, string to)
    {
        var response = await App.Client(MemoryApp.Supervisor).LiftAsync(from, "refund-window", to);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.JsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("targetScope", out _), errors.ToString());
    }

    [Fact]
    public async Task LiftToCompany_NonSupervisor_Forbidden()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var response = await ana.LiftAsync("personal:ana", "refund-window", "company");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LiftToCompany_Supervisor_Allowed()
    {
        var boss = App.Client(MemoryApp.Supervisor);
        await boss.PutCardAsync("personal:boss", "refund-window");

        var response = await boss.LiftAsync("personal:boss", "refund-window", "company");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Regression pin: lift and write share AccessPolicy.CanUpsertAsync. Today company is the only scope
    // where the two rules could differ; the test starts to discriminate once team membership lands (D055).
    [Fact]
    public async Task Lift_IntoCompany_RequiresTheSameRightsAsWritingThere()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var lift = await ana.LiftAsync("personal:ana", "refund-window", "company");
        var write = await ana.PutCardAsync("company", "refund-window");

        Assert.Equal(HttpStatusCode.Forbidden, lift.StatusCode);
        Assert.Equal(write.StatusCode, lift.StatusCode);
    }

    [Fact]
    public async Task Lift_SomeoneElsesPersonalCard_Forbidden()
    {
        await App.Client("ana").PutCardAsync("personal:ana", "refund-window");

        var response = await App.Client("bob").LiftAsync("personal:ana", "refund-window", "team:billing");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
