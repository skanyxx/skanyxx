using System.Net.Http.Json;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>D344: every change and run is audited by ids, names and states; never a prompt, an input or an answer.</summary>
public sealed class AuditTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    private const string PromptText = "PROMPT-SENTINEL-1f3a";
    private const string InputText = "INPUT-SENTINEL-9c2e";
    private const string AnswerText = "ANSWER-SENTINEL-77ab";

    [Fact]
    public async Task EveryActionIsAudited_AndNoRowHoldsPromptInputOrAnswer()
    {
        App.Agents.Answers[Researcher] = (_, _) => Task.FromResult(AnswerText);
        var workflow = await App.CreateAsync(Builder, "Audited", [Agent("s1", Researcher, $"{PromptText} {{{{input}}}}")]);
        (await App.Client(Builder).PutAsJsonAsync($"/api/investigate/workflows/{workflow.Id}",
            new { name = "Audited 2", description = "", steps = new[] { Agent("s1", Researcher, $"{PromptText} again {{{{input}}}}") } }, Json, Ct)).EnsureSuccessStatusCode();
        var run = await App.StartAsync(Builder, workflow.Id, InputText);
        await App.WaitForAsync(run.Id, RunState.Succeeded);
        App.Agents.Answers[Researcher] = async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return AnswerText; };
        var second = await App.StartAsync(Builder, workflow.Id, InputText);
        (await App.Client(Builder).PostAsync($"/api/investigate/runs/{second.Id}/cancel", null, Ct)).EnsureSuccessStatusCode();
        await App.WaitForAsync(second.Id, RunState.Cancelled);
        (await App.Client(Builder).PostAsync($"/api/investigate/workflows/{workflow.Id}/duplicate", null, Ct)).EnsureSuccessStatusCode();
        (await App.Client(Builder).DeleteAsync($"/api/investigate/workflows/{workflow.Id}", Ct)).EnsureSuccessStatusCode();

        var rows = App.Audit.Rows.ToList();
        Assert.Equal(
            ["investigate.workflow_created", "investigate.workflow_updated", "investigate.run_started", "investigate.run_finished",
             "investigate.run_started", "investigate.run_cancel_requested", "investigate.run_finished", "investigate.workflow_duplicated",
             "investigate.workflow_deleted"],
            rows.Select(r => r.Action));
        Assert.All(rows, r => Assert.Equal(Builder, r.Actor));
        Assert.Contains(rows, r => r.Action == "investigate.run_finished" && r.Details.Contains("\"state\":\"succeeded\""));
        Assert.Contains(rows, r => r.Action == "investigate.run_finished" && r.Details.Contains("\"state\":\"cancelled\""));
        Assert.Equal($"run:{run.Id}", rows[2].Target);
        foreach (var row in rows)
            foreach (var secret in new[] { PromptText, InputText, AnswerText })
                Assert.DoesNotContain(secret, row.Details);
    }
}
