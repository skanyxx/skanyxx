using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Tests.Infrastructure;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class CapacityTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    private Task<HttpResponseMessage> StartAsAsync(TicketsApp app, string user) =>
        app.Client(user).PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" });

    // CR M1: the per-user cap alone does not bound many users (or accounts), so a global cap must still hold.
    [Fact]
    public async Task ManyUserIds_ShareOneGlobalCap()
    {
        await using var app = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url,
            configure: s => s["Tickets:MaxActiveRuns"] = "3");
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-planner", Enumerable.Repeat<AgentReply>(new AgentReply.Blocked(release, "plan"), 3).ToArray());

        var statuses = new List<HttpStatusCode>();
        foreach (var user in new[] { "u1", "u2", "u3", "u4" })
            statuses.Add((await StartAsAsync(app, user)).StatusCode);
        release.SetResult();

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task ParallelStartsFromManyUsers_CannotExceedTheGlobalCap()
    {
        await using var app = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url,
            configure: s => s["Tickets:MaxActiveRuns"] = "4");
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-planner", Enumerable.Repeat<AgentReply>(new AgentReply.Blocked(release, "plan"), 20).ToArray());

        var answers = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => StartAsAsync(app, $"u{i}")));
        release.SetResult();

        Assert.Equal(4, answers.Count(a => a.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(16, answers.Count(a => a.StatusCode == HttpStatusCode.TooManyRequests));
    }

    // SEC M5: tickets has its own bounded pool, distinct from memory's (Npgsql keys pools by connection string).
    [Fact]
    public async Task TheModule_ConnectsWithItsOwnApplicationName_AndABoundedPool()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();

        var name = await db.Database.SqlQuery<string>($"SELECT current_setting('application_name') AS \"Value\"").SingleAsync();
        var settings = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());

        Assert.Equal("skanyxx-tickets", name);
        Assert.Equal(40, settings.MaxPoolSize);
    }

    [Fact]
    public void AConfiguredApplicationName_IsKept_AndStillSuffixedByTheModule()
    {
        var options = new TicketsOptions { ConnectionString = "Host=db;Application Name=replica-b" };

        Assert.Equal("replica-b-tickets", options.ConnectionSettings().ApplicationName);
    }
}
