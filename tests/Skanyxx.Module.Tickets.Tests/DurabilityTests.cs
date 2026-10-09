using Npgsql;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Engine;
using Skanyxx.Module.Tickets.Tests.Infrastructure;
using static Skanyxx.Module.Tickets.Tests.Infrastructure.Stages;

namespace Skanyxx.Module.Tickets.Tests;

/// <summary>Runs live in Postgres: a restart resumes a stage that was in flight, and a gate outlives the process.</summary>
public sealed class DurabilityTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    [Fact]
    public async Task RestartMidStage_RunsTheStageAgain_AndFinishes()
    {
        var never = new TaskCompletionSource();
        KAgent.Script("ticket-coder", new AgentReply.Blocked(never, "never returned"));
        var run = await App.StartRunAsync();
        await WaitUntil(() => KAgent.CallsTo("ticket-coder").Count == 1);

        await App.DisposeAsync();
        var before = await ReadAsync(run.Id);
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url);
        var waiting = await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Equal(RunState.Running, before.State);
        Assert.Equal(["plan", "review-plan"], before.StageRuns.Select(s => s.StageId));
        Assert.Equal(2, KAgent.CallsTo("ticket-coder").Count);
        // The interrupted attempt was never written, so the retry is attempt 1, not 2.
        Assert.Equal([1], waiting.StageRuns.Where(s => s.StageId == "code").Select(s => s.Attempt));
        Assert.Single(KAgent.CallsTo("ticket-planner"));
    }

    [Fact]
    public async Task HumanGate_SurvivesARestart()
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url);
        await Task.Delay(1500, TestContext.Current.CancellationToken); // a full poll: the worker must leave a waiting run alone
        var stillWaiting = await App.GetRunAsync(run.Id);
        await App.DecideAsync(run.Id, "approve");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal(RunState.AwaitingHuman, stillWaiting.State);
        Assert.Equal(5, done.StageRuns.Count);
        Assert.Single(KAgent.CallsTo("ticket-reviewer"));
    }

    [Fact]
    public async Task ASecondReplica_DoesNotStepRuns_WhileTheFirstHoldsTheWorkerLock()
    {
        var release = new TaskCompletionSource();
        KAgent.Script("ticket-planner", new AgentReply.Blocked(release, "plan"));
        await using var second = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url);
        var run = await App.StartRunAsync();

        await Task.Delay(2500, TestContext.Current.CancellationToken); // two polls of the second replica, while the first is inside the planner call
        release.SetResult();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Single(KAgent.CallsTo("ticket-planner"));
        Assert.Single(KAgent.CallsTo("ticket-reviewer"));
    }

    [Fact]
    public async Task WhenTheFirstReplicaStops_TheSecondTakesOver()
    {
        await using var second = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url);
        await App.DisposeAsync();

        var run = await second.StartRunAsync();
        await second.WaitForAsync(run.Id, RunState.AwaitingHuman);
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url);
    }

    [Fact]
    public async Task AStoppedLeader_ReleasesTheWorkerLock_EvenThoughTheProcessStaysUp()
    {
        await WaitUntilAsync(async () => await LeaderAsync() is not null);

        await App.DisposeAsync();

        // A pooled leader connection would keep its session, and the lock, idle in this process's pool.
        await WaitUntilAsync(async () => await LeaderAsync() is null);
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url);
    }

    [Fact]
    public async Task ALeaderWhoseSessionIsKilled_LosesTheLock_AndAnotherReplicaTakesOver()
    {
        await WaitUntilAsync(async () => await LeaderAsync() is not null);
        await using var second = await TicketsApp.StartAsync(Replica("replica-b"), KAgent.Url);
        var first = (await LeaderAsync())!;

        await TerminateAsync(first.Value.Pid);
        await WaitUntilAsync(async () => (await LeaderAsync())?.App == "replica-b-tickets");
        var run = await second.StartRunAsync();
        await second.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.Single(KAgent.CallsTo("ticket-planner"));
    }

    [Fact]
    public async Task ALeaderThatLostItsLockMidSweep_StepsNoFurtherRun()
    {
        await App.SavePipelineAsync("one", Pipeline(Stage("plan", "plan", "ticket-planner")));
        await WaitUntilAsync(async () => await LeaderAsync() is not null);
        await using var second = await TicketsApp.StartAsync(Replica("replica-b"), KAgent.Url);
        TaskCompletionSource first = new(), inFlight = new(), secondsRun = new();
        KAgent.Script("ticket-planner",
            new AgentReply.Blocked(first, "p0"), new AgentReply.Blocked(inFlight, "p1"),
            new AgentReply.Completed("p1 again"), new AgentReply.Blocked(secondsRun, "p2"));

        // run0 holds the first replica's worker, so run1 and run2 land in ONE sweep of it.
        var run0 = await App.StartRunAsync(pipelineId: "one");
        await WaitUntil(() => KAgent.CallsTo("ticket-planner").Count == 1);
        await App.StartRunAsync(pipelineId: "one");
        var run2 = await App.StartRunAsync(pipelineId: "one");
        first.SetResult();
        await App.WaitForAsync(run0.Id, RunState.Succeeded);
        await WaitUntil(() => KAgent.CallsTo("ticket-planner").Count == 2);

        // The first replica is inside run1's step; its session dies and the second replica takes both runs.
        await TerminateAsync((await LeaderAsync())!.Value.Pid);
        await WaitUntil(() => KAgent.CallsTo("ticket-planner").Count == 4);
        inFlight.SetResult();
        await WaitUntilAsync(() => Task.FromResult(App.Errors.Any(e => e.Contains("stepping down"))));
        secondsRun.SetResult();
        await App.WaitForAsync(run2.Id, RunState.Succeeded);

        // Paid twice: run1 (in flight on failover, accepted). Not run2: the first replica checked its lock first.
        Assert.Equal(4, KAgent.CallsTo("ticket-planner").Count);
    }

    [Fact]
    public async Task ReplicasStartingTogether_MigrateAndSeedOneAtATime()
    {
        await using var probe = await OpenProbeAsync();
        await using (var hold = new NpgsqlCommand($"SELECT pg_advisory_lock({TicketsMigrator.MigrateLockKey})", probe))
            await hold.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        var starting = TicketsApp.StartAsync(Replica("replica-b"), KAgent.Url);
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        var waited = !starting.IsCompleted;
        await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({TicketsMigrator.MigrateLockKey})", probe))
            await release.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        await using var second = await starting;

        Assert.True(waited, "the migrator did not wait for the migration lock");
    }

    private string Replica(string name) => new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { ApplicationName = name }.ConnectionString;

    /// <summary>An unpooled connection of its own, so it never shares a session with the app under test.</summary>
    private async Task<NpgsqlConnection> OpenProbeAsync()
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { ApplicationName = "probe", Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>The session holding the worker lock: its backend pid and application name.</summary>
    private async Task<(int Pid, string App)?> LeaderAsync()
    {
        await using var probe = await OpenProbeAsync();
        await using var command = new NpgsqlCommand(
            "SELECT l.pid, a.application_name FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid " +
            $"WHERE l.locktype = 'advisory' AND l.granted AND l.objsubid = 1 AND l.objid::bigint = {RunWorker.LeaderLockKey}", probe);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetInt32(0), reader.GetString(1)) : null;
    }

    private async Task TerminateAsync(int pid)
    {
        await using var probe = await OpenProbeAsync();
        await using var command = new NpgsqlCommand($"SELECT pg_terminate_backend({pid})", probe);
        await command.ExecuteScalarAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(100);
        }
    }

    private async Task<Run> ReadAsync(Guid id)
    {
        await using var db = Postgres.CreateDbContext();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(db.Runs, r => r.StageRuns), r => r.Id == id);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(50);
        }
    }
}
