using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Reports;
using Skanyxx.Module.Investigate.Rules;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.ScriptedAgents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>
/// D556: a step whose kagent task ended badly keeps kagent's own reason (cleaned) beside the fixed category. The run
/// panel and both reports show it to builders and up; an employee reaches none of them, and a viewer outside those
/// roles would get nothing either.
/// </summary>
public sealed class FailureReasonTests(PostgresFixture postgres) : InvestigateTestBase(postgres)
{
    private static readonly string Reason = ChatFailureReason.Of(
        "failed to list MCP tools: Post \"http://host.docker.internal:5287/mcp/memory\": dial tcp 0.250.250.254:5287: connect: connection refused")!;

    [Fact]
    public async Task AFailedStep_ShowsKagentsReason_InThePanelAndBothReports_ToBuildersAndUp()
    {
        App.Agents.Answers[Researcher] = (_, _) => throw new AgentCallException("the agent's task ended 'failed'", "task-1", "failed", Reason);
        var workflow = await App.CreateAsync(Builder, "Fails", [Agent("s1", Researcher, "{{input}}", "Look")]);
        var run = await App.StartAsync(Builder, workflow.Id, "why?");
        await App.WaitForAsync(run.Id, RunState.Failed);

        foreach (var user in new[] { Builder, Owner })
        {
            var step = Assert.Single((await App.GetRunAsync(run.Id, user)).StepRuns);
            Assert.Equal("the agent's task ended 'failed'", step.Error);
            Assert.Equal(Reason, step.KagentReason);
        }
        var json = await App.Client(Builder).GetFromJsonAsync<JsonElement>($"/api/investigate/runs/{run.Id}/report.json", Ct);
        Assert.Equal(Reason, json.GetProperty("steps")[0].GetProperty("kagentReason").GetString());
        var pdf = Encoding.Latin1.GetString(await App.Client(Builder).GetByteArrayAsync($"/api/investigate/runs/{run.Id}/report.pdf", Ct));
        Assert.Contains("kagent said: failed to list MCP tools", pdf);

        foreach (var path in new[] { $"runs/{run.Id}", $"runs/{run.Id}/report.json", $"runs/{run.Id}/report.pdf" })
        {
            var asEmployee = await App.Client(Employee).GetAsync($"/api/investigate/{path}", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, asEmployee.StatusCode);
            Assert.DoesNotContain("host.docker.internal", await asEmployee.Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public void AViewerOutsideTheInternalsRoles_GetsNoReason_InTheRunOrTheReport()
    {
        var run = new InvestigationRun
        {
            Id = Guid.NewGuid(), WorkflowName = "w", CreatedBy = Builder,
            Steps = [Agent("s1", Researcher, "{{input}}")],
            StepRuns = [new StepRun { StepId = "s1", State = StepState.Failed, Error = "the agent's task ended 'failed'", KagentReason = Reason }]
        };
        var outsider = new Viewer(Builder, IsSupervisor: false, IsOwner: false, SeesAgentInternals: false);
        var insider = outsider with { SeesAgentInternals = true };

        Assert.Null(Assert.Single(RunMapper.Full(run, outsider).StepRuns).KagentReason);
        Assert.Null(Assert.Single(RunReport.From(run, outsider).Steps).KagentReason);
        Assert.DoesNotContain("host.docker.internal", Encoding.Latin1.GetString(PdfReport.Render(RunReport.From(run, outsider))));
        Assert.Equal(Reason, Assert.Single(RunMapper.Full(run, insider).StepRuns).KagentReason);
        Assert.Equal(Reason, Assert.Single(RunReport.From(run, insider).Steps).KagentReason);
    }
}
