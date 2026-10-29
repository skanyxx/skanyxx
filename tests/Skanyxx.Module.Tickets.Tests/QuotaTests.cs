using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Tests.Infrastructure;

namespace Skanyxx.Module.Tickets.Tests;

/// <summary>D383: every stage turn counts against the token quota of whoever started the run; over it, no new run starts.</summary>
public sealed class QuotaTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    private sealed class RecordingQuota : IChatQuota
    {
        public static volatile bool Refuse;

        public static ConcurrentQueue<ChatQuotaUsage> Recorded { get; } = new();

        public Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct) =>
            Task.FromResult(Refuse ? ChatQuotaAdmission.Refused("Over your token limit: use Request more.") : ChatQuotaAdmission.Admitted());

        public Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct)
        {
            Recorded.Enqueue(usage);
            return Task.CompletedTask;
        }
    }

    private async Task WithRecordingQuotaAsync()
    {
        RecordingQuota.Refuse = false;
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
            Assert.True(u.Usage.Total > 0);
        });
    }

    [Fact]
    public async Task APersonOverTheirQuota_StartsNoNewRun()
    {
        await WithRecordingQuotaAsync();
        RecordingQuota.Refuse = true;

        var response = await App.Client().PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("Request more", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(RecordingQuota.Recorded);
    }
}
