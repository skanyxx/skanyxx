using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Tests.Infrastructure;
using static Skanyxx.Module.Tickets.Tests.Infrastructure.Stages;

namespace Skanyxx.Module.Tickets.Tests;

/// <summary>
/// D383, D387: every stage turn counts against the token quota of whoever started the run; over it, no new run starts and
/// no gate decision sends a run on to more stages. A chat answer on its way never refuses a run.
/// </summary>
public sealed class QuotaTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    private sealed class RecordingQuota : IChatQuota
    {
        public static volatile bool Refuse;

        /// <summary>Two chat answers on their way: no in-flight place left, though under the limit.</summary>
        public static volatile bool Busy;

        public static ConcurrentQueue<ChatQuotaUsage> Recorded { get; } = new();

        public Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct) =>
            Task.FromResult(Refuse ? ChatQuotaAdmission.Refused("Over your token limit: use Request more.")
                : Busy ? ChatQuotaAdmission.Refused("You already have 2 answers on their way.")
                : ChatQuotaAdmission.Admitted());

        public Task<string?> RefusalAsync(string userId, CancellationToken ct) =>
            Task.FromResult(Refuse ? "Over your token limit: use Request more." : null);

        public Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct)
        {
            Recorded.Enqueue(usage);
            return Task.CompletedTask;
        }
    }

    private async Task WithRecordingQuotaAsync()
    {
        RecordingQuota.Refuse = false;
        RecordingQuota.Busy = false;
        RecordingQuota.Recorded.Clear();
        await App.DisposeAsync();
        App = await TicketsApp.StartAsync(Postgres.ConnectionString, KAgent.Url, services => services.AddScoped<IChatQuota, RecordingQuota>());
    }

    [Fact]
    public async Task EveryStageTurn_IsCountedForTheRunsStarter()
    {
        await WithRecordingQuotaAsync();

        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);

        Assert.NotEmpty(RecordingQuota.Recorded);
        Assert.All(RecordingQuota.Recorded, u =>
        {
            Assert.Equal(TicketsApp.User, u.UserId);
            Assert.Equal(run.Id.ToString(), u.ConversationId);
            Assert.Equal("kagent/default-model-config", u.Model);
            Assert.True(u.Usage.Total > 0);
        });
    }

    [Fact]
    public async Task APersonOverTheirQuota_StartsNoNewRun()
    {
        await WithRecordingQuotaAsync();
        RecordingQuota.Refuse = true;

        var response = await App.Starter().PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("Request more", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(RecordingQuota.Recorded);
    }

    [Fact]
    public async Task ChatAnswersOnTheirWay_DoNotRefuseARun()
    {
        await WithRecordingQuotaAsync();
        RecordingQuota.Busy = true;

        var response = await App.Client().PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" }, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}");
        Assert.DoesNotContain("answers", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGateDecisionThatSendsTheRunOn_IsRefusedOverTheStartersQuota_AndTheRunStaysAtItsGate()
    {
        await WithRecordingQuotaAsync();
        await App.SavePipelineAsync("gated-loop", Pipeline(
            Stage("code", "code", "ticket-coder"),
            Stage("review", "review", "ticket-reviewer", ("gate", "human"), ("onFail", "goto"), ("goto", "code"), ("maxLoops", 1))));
        var run = await App.StartRunAsync(pipelineId: "gated-loop");
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);
        RecordingQuota.Refuse = true;

        var byStarter = await App.DecideAsync(run.Id, "reject", "again");
        var bySupervisor = await App.DecideAsync(run.Id, "reject", "again", TicketsApp.Supervisor);
        var parked = await App.GetRunAsync(run.Id);
        var approved = await App.DecideAsync(run.Id, "approve");

        Assert.Equal(HttpStatusCode.TooManyRequests, byStarter.StatusCode);
        Assert.Contains("Request more", await byStarter.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.TooManyRequests, bySupervisor.StatusCode);
        Assert.Contains("The person who started this run", await bySupervisor.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RunState.AwaitingHuman, parked.State);
        Assert.Single(KAgent.CallsTo("ticket-coder"));
        // Approving the last stage ends the run: no more turns, so nothing to refuse.
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(RunState.Succeeded, (await App.WaitForAsync(run.Id, RunState.Succeeded)).State);
    }
}
