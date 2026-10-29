using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>D385: every step's turn is admitted against the run starter's token quota and counted to them once.</summary>
public sealed class QuotaTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    private const string Refusal = "Over your token limit: use Request more.";

    private sealed class RecordingQuota : IChatQuota
    {
        public volatile bool Refuse;

        public ConcurrentQueue<string> Admitted { get; } = new();

        public ConcurrentQueue<ChatQuotaUsage> Recorded { get; } = new();

        public Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct)
        {
            Admitted.Enqueue(userId);
            return Task.FromResult(Refuse ? ChatQuotaAdmission.Refused(Refusal) : ChatQuotaAdmission.Admitted());
        }

        public Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct)
        {
            Recorded.Enqueue(usage);
            return Task.CompletedTask;
        }
    }

    private readonly RecordingQuota _quota = new();

    private async Task WithRecordingQuotaAsync()
    {
        await App.DisposeAsync();
        App = await InvestigateApp.StartAsync(Postgres.ConnectionString, services: s => s.AddSingleton<IChatQuota>(_quota));
    }

    [Fact]
    public async Task ARefusedStep_CallsNoAgent_AndFailsTheRunWithTheReason()
    {
        await WithRecordingQuotaAsync();
        _quota.Refuse = true;
        var workflow = await App.CreateAsync(Builder, "Over", [Agent("s1", Researcher, "Find: {{input}}", "Look"), Agent("s2", Writer, "{{steps.s1}}")]);

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded, RunState.Failed);

        Assert.Equal(RunState.Failed, done.State);
        Assert.Equal($"Step 'Look' ({Researcher}) failed: Over your token limit: use Request more.", done.Failure);
        Assert.Equal("Over your token limit: use Request more", done.StepRuns.Single(s => s.StepId == "s1").Error);
        Assert.Equal(StepState.Skipped, done.StepRuns.Single(s => s.StepId == "s2").State);
        Assert.Empty(App.Agents.Calls);
        Assert.Equal([Builder], _quota.Admitted);
        Assert.Empty(_quota.Recorded);
    }

    [Fact]
    public async Task EverySucceededStep_IsCountedOnceForTheStarter_WithKAgentsTokens()
    {
        await WithRecordingQuotaAsync();
        App.Agents.Usage = new ChatUsage(40, 10, 50);
        var workflow = await App.CreateAsync(Builder, "Two", [Agent("s1", Researcher, "a"), Agent("s2", Writer, "b")]);

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal([Researcher, Writer], _quota.Recorded.Select(u => u.AgentRef));
        Assert.All(_quota.Recorded, u =>
        {
            Assert.Equal(Builder, u.UserId);
            Assert.Equal(run.Id.ToString(), u.ConversationId);
            Assert.Equal("unknown", u.Model);
            Assert.Equal(new ChatUsage(40, 10, 50), u.Usage);
            Assert.False(u.Estimated);
        });
        Assert.Equal([Builder, Builder], _quota.Admitted);
    }

    [Fact]
    public async Task AFailedStep_AndTheSiblingItCancels_AreEachCountedOnce_AsAnEstimate()
    {
        await WithRecordingQuotaAsync();
        App.Agents.Answers[Analyst] = (_, _) => throw new AgentCallException("kagent answered HTTP 500");
        App.Agents.Answers[Researcher] = async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(20), ct); return "never"; };
        var workflow = await App.CreateAsync(Builder, "Breaks",
            [Group("g1", GroupMode.Parallel, Agent("slow", Researcher, "x"), Agent("bad", Analyst, "y")), Agent("after", Writer, "z")]);

        var run = await App.StartAsync(Builder, workflow.Id, "x");
        await App.WaitForAsync(run.Id, RunState.Failed);

        Assert.Equal([Analyst, Researcher], _quota.Recorded.Select(u => u.AgentRef).Order());
        Assert.All(_quota.Recorded, u =>
        {
            Assert.Equal(Builder, u.UserId);
            Assert.True(u.Estimated);
            Assert.True(u.Usage.Prompt > 0);
            Assert.Equal(0L, u.Usage.Completion);
        });
    }
}
