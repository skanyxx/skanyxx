using System.Net;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class ValidationTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Fact]
    public async Task EveryInvalidField_ReportedTogether_AndNothingWritten()
    {
        var response = await App.Client("ana").PutCardAsync(
            "global", "Bad_Key", type: "nope", what: new string('w', 201), why: new string('y', 401));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.JsonAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name).ToList();
        foreach (var field in new[] { "scope", "key", "type", "what", "why" })
            Assert.Contains(field, errors, StringComparer.OrdinalIgnoreCase);

        await using var db = Postgres.CreateDbContext();
        Assert.Empty(db.Cards);
    }

    [Theory]
    [InlineData(200, 400, HttpStatusCode.Created)]
    [InlineData(201, 400, HttpStatusCode.BadRequest)]
    [InlineData(200, 401, HttpStatusCode.BadRequest)]
    public async Task WhatAndWhyCaps_AreExact(int whatLength, int whyLength, HttpStatusCode expected)
    {
        var response = await App.Client("ana").PutCardAsync(
            "personal:ana", "caps", what: new string('w', whatLength), why: new string('y', whyLength));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task MissingIdentity_Rejected()
    {
        var response = await App.Client().PutCardAsync("personal:ana", "refund-window");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.JsonAsync()).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals("caller.identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EmptySearchQuery_Rejected()
    {
        var response = await App.Client("ana").GetAsync("/api/memory/cards?q=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
