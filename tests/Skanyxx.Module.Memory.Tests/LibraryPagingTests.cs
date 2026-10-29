using System.Diagnostics;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D355: the library filters, sorts, counts and pages on the server, correctly and quickly at scale. 5,000 cards: every
/// fifth in team billing (ana's team), the rest published company cards; types, writers, tags and update times cycle,
/// and every two cards share an update time, so paging must break ties the same way on every page.
/// </summary>
public sealed class LibraryPagingTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private const int Count = 5_000;
    private static readonly string[] Types = ["decision", "fact", "procedure", "open"];
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly LibraryUser Ana = People.Person("ana");
    private static readonly LibraryUser Bob = People.Person("bob");

    private sealed record Seeded(int I, string Scope, string Key, string Type, string Who, DateTime UpdatedAt, bool Tagged, bool Zebra);

    private static readonly IReadOnlyList<Seeded> Cards = [.. Enumerable.Range(0, Count).Select(i => new Seeded(
        i, i % 5 == 0 ? "team:billing" : "company", $"card-{i:D4}", Types[i % 4], i % 3 == 0 ? "ana" : "bob",
        Start.AddHours(i / 2), i % 10 == 0, i % 7 == 3))];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        App.Org.Join("ana", "billing", "finance");
        await using var db = Postgres.CreateDbContext();
        db.Cards.AddRange(Cards.Select(c => new Card
        {
            Scope = c.Scope, Key = c.Key, Version = 1, Type = Enum.Parse<CardType>(c.Type, true),
            What = $"Card {c.I:D4} about topic {c.I % 7}{(c.Zebra ? " zebra" : "")}", Why = "Seeded for paging", Who = c.Who,
            UpdatedAt = c.UpdatedAt, CreatedAt = c.UpdatedAt, Status = CardStatus.Published, Origin = CardOrigin.Manual,
            Keywords = c.Tagged ? ["refunds"] : []
        }));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PagingNewestFirst_ReachesEveryCardOnce_InOrder_AndQuickly()
    {
        var expected = Cards.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Scope, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => c.Key).ToList();
        var seen = new List<string>();
        var slowest = TimeSpan.Zero;

        for (var offset = 0; ; offset += LibraryShelf.Limit)
        {
            var watch = Stopwatch.StartNew();
            var shelf = await BrowseAsync(Ana, new LibraryFilter(Offset: offset, Sort: "newest"));
            slowest = watch.Elapsed > slowest ? watch.Elapsed : slowest;
            Assert.Equal(Count, shelf.Total);
            Assert.Equal(offset, shelf.Offset);
            seen.AddRange(shelf.Cards.Select(c => c.Key));
            if (!shelf.HasMore)
                break;
        }

        Assert.Equal(expected, seen);
        Assert.True(slowest < TimeSpan.FromSeconds(1), $"slowest page took {slowest.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Filters_CountExactly_WhatThePagesHold()
    {
        var fact = await BrowseAsync(Ana, new LibraryFilter(Type: "fact"));
        var anaFacts = await BrowseAsync(Ana, new LibraryFilter(Type: "fact", Owner: "ana"));
        var tagged = await BrowseAsync(Ana, new LibraryFilter(Keyword: "refunds"));
        var from = DateOnly.FromDateTime(Start.AddDays(10));
        var to = DateOnly.FromDateTime(Start.AddDays(19));
        var dated = await BrowseAsync(Ana, new LibraryFilter(From: from, To: to, Sort: "oldest"));
        var team = await BrowseAsync(Ana, new LibraryFilter(), scope: "team:billing");
        var zebra = await BrowseAsync(Ana, new LibraryFilter(Sort: "oldest"), text: "zebra");
        var bob = await BrowseAsync(Bob, new LibraryFilter());

        Assert.Equal(Cards.Count(c => c.Type == "fact"), fact.Total);
        Assert.Equal(Types.ToDictionary(t => t, t => Cards.Count(c => c.Type == t)), fact.TypeCounts); // facets ignore the type
        Assert.All(fact.Cards, c => Assert.Equal("fact", c.Type));
        Assert.Equal(Cards.Count(c => c.Type == "fact" && c.Who == "ana"), anaFacts.Total);
        Assert.Equal(Count / 10, tagged.Total);
        var inRange = Cards.Where(c => DateOnly.FromDateTime(c.UpdatedAt) >= from && DateOnly.FromDateTime(c.UpdatedAt) <= to).ToList();
        Assert.Equal(inRange.Count, dated.Total);
        Assert.Equal(inRange.OrderBy(c => c.UpdatedAt).ThenBy(c => c.Scope, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(LibraryShelf.Limit).Select(c => c.Key), dated.Cards.Select(c => c.Key));
        Assert.Equal(Count / 5, team.Total);
        Assert.All(team.Cards, c => Assert.Equal("team:billing", c.Scope));
        Assert.Equal(Cards.Count(c => c.Zebra), zebra.Total);
        Assert.Equal(Cards.Where(c => c.Zebra).OrderBy(c => c.UpdatedAt).ThenBy(c => c.Scope, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(LibraryShelf.Limit).Select(c => c.Key), zebra.Cards.Select(c => c.Key));
        Assert.Equal(Cards.Count(c => c.Scope == "company"), bob.Total); // not in billing: its cards are neither listed nor counted
    }

    [Fact]
    public async Task TheLastPage_AndPastTheEnd_AreConsistent()
    {
        var last = await BrowseAsync(Ana, new LibraryFilter(Offset: Count - 20));
        var past = await BrowseAsync(Ana, new LibraryFilter(Offset: Count + 50));

        Assert.Equal(20, last.Cards.Count);
        Assert.False(last.HasMore);
        Assert.Empty(past.Cards);
        Assert.Equal(Count, past.Total);
    }

    [Fact]
    public async Task BadSortsAndRanges_AreRefused()
    {
        await Assert.ThrowsAsync<ValidationException>(() => BrowseAsync(Ana, new LibraryFilter(Sort: "random()")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            BrowseAsync(Ana, new LibraryFilter(From: new DateOnly(2026, 2, 1), To: new DateOnly(2026, 1, 1))));
        await Assert.ThrowsAsync<ValidationException>(() => BrowseAsync(Ana, new LibraryFilter(Offset: LibraryFilter.MaxOffset + 1)));
    }

    private async Task<LibraryShelf> BrowseAsync(LibraryUser user, LibraryFilter filter, string? scope = null, string? text = null)
    {
        await using var services = App.Services.CreateAsyncScope();
        return (await services.ServiceProvider.GetRequiredService<IMediator>().Send(new BrowseLibraryQuery(user, text, scope, filter))).Value!;
    }
}
