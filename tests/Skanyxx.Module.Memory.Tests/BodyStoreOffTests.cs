using System.Net;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>No <c>Memory:ObjectStore</c> (the default): bodies stay in Postgres with today's cap (D180).</summary>
public sealed class BodyStoreOffTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task ALargeBody_StaysWholeInPostgres_WithItsSource()
    {
        var ana = App.Client("ana");
        var body = new string('x', CardLimits.Body);

        var card = await (await ana.PutCardAsync("personal:ana", "big", body: body, source: "chat://42")).CardAsync();
        var served = await ana.GetAsync("/api/memory/cards/personal:ana/big/body", TestContext.Current.CancellationToken);

        Assert.Equal(body, card.Body);
        Assert.Equal("chat://42", card.Source);
        Assert.Equal(body, await served.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABodyOverTheCap_IsRefusedAsBefore()
    {
        var response = await App.Client("ana").PutCardAsync("personal:ana", "big", body: new string('x', CardLimits.Body + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty("body", out _));
    }
}
