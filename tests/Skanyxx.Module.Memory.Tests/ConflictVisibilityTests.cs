using Skanyxx.Core.Platform;
using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Features;
using Skanyxx.Module.Memory.Features.Cards;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class ConflictVisibilityTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private async Task<Outcome<Card>> SendAsync(UpsertCardCommand command)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }

    [Fact]
    public async Task UpsertOnlyAgent_ConflictDoesNotRevealTheCurrentCard()
    {
        await App.SupervisorClient().SetGrantsAsync("blind", new { scope = "company", canSearch = false, canUpsert = true });
        await App.SupervisorClient().PutCardAsync("company", "refund-window", body: "SECRET");

        var outcome = await SendAsync(new UpsertCardCommand(
            new MemoryCaller(null, "blind"), "company", "refund-window", 0, "fact", "w", "y"));

        Assert.Equal(OutcomeStatus.Conflict, outcome.Status);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task SearchingAgent_ConflictIncludesTheCurrentCard()
    {
        await App.SupervisorClient().SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        await App.SupervisorClient().PutCardAsync("company", "refund-window");

        var outcome = await SendAsync(new UpsertCardCommand(
            new MemoryCaller(null, "writer"), "company", "refund-window", 0, "fact", "w", "y"));

        Assert.Equal(OutcomeStatus.Conflict, outcome.Status);
        Assert.Equal(1, outcome.Value!.Version);
    }

    [Fact]
    public async Task LiftingAnUnpublishedCard_ConflictsWithoutACard()
    {
        App.Org.Join("ana", "billing", "finance");
        await App.Client("ana").PutCardAsync("personal:ana", "draft");
        await using (var db = Postgres.CreateDbContext())
        {
            var card = db.Cards.Single();
            card.Status = CardStatus.Candidate;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await App.Client("ana").LiftAsync("personal:ana", "draft", "team:billing");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await response.JsonAsync()).GetProperty("current").ValueKind);
    }
}
