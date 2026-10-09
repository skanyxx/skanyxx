using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// CR M2: the rename races that slip past the handler's checks end in a 409, never a 500. To force them, the test holds
/// the rows' locks: both renames read the same state, pass every check, and queue on their UPDATE; released, one wins and
/// the other meets the version token (same card) or the unique (scope, key) index (two cards, one new key).
/// </summary>
public sealed class RenameRaceTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task SameVersionTwice_OneRenames_TheOtherIsStale()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-windw");

        var responses = await RaceAsync(["refund-windw"],
            () => RenameAsync(ana, "refund-windw", "refund-window"),
            () => RenameAsync(ana, "refund-windw", "refund-period"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var loser = (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).JsonAsync());
        Assert.Equal("stale", loser.GetProperty("reason").GetString());
        Assert.Equal(2, loser.GetProperty("current").GetProperty("version").GetInt32());
        await using var db = Postgres.CreateDbContext();
        var card = Assert.Single(db.Cards);
        Assert.Equal(2, card.Version);
    }

    [Fact]
    public async Task TwoCardsToOneNewKey_OneRenames_TheOtherFindsItTaken()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-windw");
        await ana.PutCardAsync("personal:ana", "refnd-window");

        var responses = await RaceAsync(["refund-windw", "refnd-window"],
            () => RenameAsync(ana, "refund-windw", "refund-window"),
            () => RenameAsync(ana, "refnd-window", "refund-window"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var loser = (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).JsonAsync());
        Assert.Equal("taken", loser.GetProperty("reason").GetString());
        Assert.Equal("refund-window", loser.GetProperty("current").GetProperty("key").GetString());
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(1, db.Cards.Count(c => c.Key == "refund-window"));
    }

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, string key, string newKey) =>
        client.PostAsJsonAsync($"/api/memory/cards/personal:ana/{key}/rename", new { newKey, version = 1 });

    /// <summary>Starts both renames while <paramref name="keys"/> are row-locked, waits until both queue on a lock, releases.</summary>
    private async Task<HttpResponseMessage[]> RaceAsync(string[] keys, Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        await using var holder = new NpgsqlConnection(Postgres.ConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        await using (var lockRows = new NpgsqlCommand("SELECT id FROM memory_cards WHERE key = ANY(@keys) FOR UPDATE", holder, tx))
        {
            lockRows.Parameters.AddWithValue("keys", keys);
            await lockRows.ExecuteNonQueryAsync();
        }

        var renames = new[] { first(), second() };
        await WaitForLockWaitersAsync(2);
        await tx.RollbackAsync();
        return await Task.WhenAll(renames);
    }

    private async Task WaitForLockWaitersAsync(int count)
    {
        await using var monitor = new NpgsqlConnection(Postgres.ConnectionString);
        await monitor.OpenAsync();
        await using var waiting = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", monitor);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((long)(await waiting.ExecuteScalarAsync())! < count)
        {
            Assert.True(DateTime.UtcNow < deadline, "The renames never reached their UPDATE.");
            await Task.Delay(20);
        }
    }
}
