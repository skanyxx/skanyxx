using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_IsIdempotent_AndModelHasNoPendingChanges()
    {
        await using var db = postgres.CreateDbContext();

        await db.Database.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task VersionConcurrencyToken_RejectsTheSecondWriterOfTheSameVersion()
    {
        await postgres.ResetAsync();
        await using (var seed = postgres.CreateDbContext())
        {
            seed.Cards.Add(new Card
            {
                Scope = "company", Key = "race", Version = 1, Type = CardType.Fact,
                What = "w", Why = "y", Who = "t", UpdatedAt = DateTime.UtcNow, Status = CardStatus.Published
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var first = postgres.CreateDbContext();
        await using var second = postgres.CreateDbContext();
        var a = await first.Cards.SingleAsync(c => c.Key == "race", cancellationToken: TestContext.Current.CancellationToken);
        var b = await second.Cards.SingleAsync(c => c.Key == "race", cancellationToken: TestContext.Current.CancellationToken);
        a.What = "first"; a.Version++;
        b.What = "second"; b.Version++;

        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
