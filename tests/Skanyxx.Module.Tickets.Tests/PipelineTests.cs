using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Tests.Infrastructure;
using static Skanyxx.Module.Tickets.Tests.Infrastructure.Stages;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class PipelineTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    [Fact]
    public async Task DefaultPipeline_IsSeeded_WithTheLoopAndTheGate()
    {
        var pipeline = await App.Client().GetFromJsonAsync<Pipeline>("/api/tickets/pipelines/ticket-fix", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["plan", "review-plan", "code", "qa-code", "review"], pipeline!.Stages.Select(s => s.Id));
        var qa = pipeline.Stages[3];
        Assert.Equal((OnFail.Goto, "code", 2, true), (qa.OnFail, qa.Goto, qa.MaxLoops, qa.FailOnVerdict));
        Assert.Equal(Gate.Human, pipeline.Stages[4].Gate);
    }

    [Fact]
    public async Task Pipeline_UsesSnakeCaseEnumNames_OnTheWire()
    {
        var json = await App.Client().GetStringAsync("/api/tickets/pipelines/ticket-fix", TestContext.Current.CancellationToken);

        Assert.Contains("\"onFail\":\"goto\"", json);
        Assert.Contains("\"gate\":\"human\"", json);
        Assert.Contains("\"kind\":\"qa\"", json);
        Assert.DoesNotContain("\"namespace\"", json);
    }

    [Fact]
    public async Task OnlyASupervisor_SavesPipelines()
    {
        var response = await App.SavePipelineAsync("mine", Pipeline(Stage("plan", "plan", "ticket-planner")), userId: TicketsApp.User);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Save_CreatesThenReplaces()
    {
        var created = await App.SavePipelineAsync("mine", Pipeline(Stage("plan", "plan", "ticket-planner")));
        var replaced = await App.SavePipelineAsync("mine", Pipeline(Stage("code", "code", "ticket-coder")));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var saved = await App.Client().GetFromJsonAsync<Pipeline>("/api/tickets/pipelines/mine", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["code"], saved!.Stages.Select(s => s.Id));
    }

    public static TheoryData<string, object> Invalid => new()
    {
        { "goto forward", Pipeline(Stage("qa", "qa", "ticket-qa", ("onFail", "goto"), ("goto", "code")), Stage("code", "code", "ticket-coder")) },
        { "goto itself", Pipeline(Stage("qa", "qa", "ticket-qa", ("onFail", "goto"), ("goto", "qa"))) },
        { "goto missing", Pipeline(Stage("code", "code", "ticket-coder"), Stage("qa", "qa", "ticket-qa", ("onFail", "goto"), ("maxLoops", 1))) },
        { "goto without on_fail goto", Pipeline(Stage("code", "code", "ticket-coder"), Stage("qa", "qa", "ticket-qa", ("goto", "code"))) },
        { "max_loops over 10", Pipeline(Stage("code", "code", "ticket-coder"), Stage("qa", "qa", "ticket-qa", ("onFail", "goto"), ("goto", "code"), ("maxLoops", 11))) },
        { "goto without max_loops", Pipeline(Stage("code", "code", "ticket-coder"), Stage("qa", "qa", "ticket-qa", ("onFail", "goto"), ("goto", "code"))) },
        { "NUL in instructions", Pipeline(Stage("a", "plan", "ticket-planner", ("instructions", "a\u0000b"))) },
        { "unknown kind", Pipeline(Stage("x", "deploy", "ticket-coder")) },
        { "numeric kind", Pipeline(Stage("x", "plan", "ticket-coder", ("kind", 2))) },
        { "unknown gate", Pipeline(Stage("x", "plan", "ticket-coder", ("gate", "robot"))) },
        { "duplicate stage ids", Pipeline(Stage("a", "plan", "ticket-planner"), Stage("a", "code", "ticket-coder")) },
        { "null skill", Pipeline(Stage("a", "plan", "ticket-planner", ("skills", new string?[] { null }))) },
        { "unknown skill", Pipeline(Stage("a", "plan", "ticket-planner", ("skills", new[] { "acceptance_criteria" }))) },
        { "no agents", Pipeline(Stage("a", "plan", "ticket-planner", ("agents", Array.Empty<object>()))) },
        { "agent without namespace", Pipeline(Stage("a", "plan", "ticket-planner", ("agents", new[] { new { id = "p", agent = "ticket-planner" } }))) },
        { "agent path traversal", Pipeline(Stage("a", "plan", "ticket-planner", ("agents", new[] { new { id = "p", agent = "kagent/../admin" } }))) },
        { "agent not allowed", Pipeline(Stage("a", "plan", "ticket-planner", ("agents", new[] { new { id = "p", agent = "kube-system/admin" } }))) },
        { "kagent's own tool agent", Pipeline(Stage("a", "plan", "ticket-planner", ("agents", new[] { new { id = "p", agent = "kagent/k8s-agent" } }))) },
        { "duplicate agent ids", Pipeline(Stage("a", "plan", "ticket-planner", ("agents", new[] { new { id = "p", agent = "kagent/a" }, new { id = "p", agent = "kagent/b" } }))) },
        { "bad stage id", Pipeline(Stage("Bad Id", "plan", "ticket-planner")) },
        { "no stages", Pipeline() },
        { "null stage", new { name = "n", description = "", stages = new object?[] { null } } },
        { "no name", new { name = "", description = "", stages = new[] { Stage("a", "plan", "ticket-planner") } } }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task InvalidPipelines_AreRejected_AndNothingIsSaved(string why, object pipeline)
    {
        var response = await App.SavePipelineAsync("bad", pipeline);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{why}: {response.StatusCode} {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        Assert.Equal(HttpStatusCode.NotFound, (await App.Client().GetAsync("/api/tickets/pipelines/bad", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Delete_IsForSupervisors_AndRemovesThePipeline()
    {
        await App.SavePipelineAsync("gone", Pipeline(Stage("plan", "plan", "ticket-planner")));

        var byUser = await App.Client().DeleteAsync("/api/tickets/pipelines/gone", TestContext.Current.CancellationToken);
        var bySupervisor = await App.Client(TicketsApp.Supervisor).DeleteAsync("/api/tickets/pipelines/gone", TestContext.Current.CancellationToken);
        var again = await App.Client(TicketsApp.Supervisor).DeleteAsync("/api/tickets/pipelines/gone", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, byUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bySupervisor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task BadPipelineId_InTheRoute_IsRejected()
    {
        var response = await App.SavePipelineAsync("BAD_ID", Pipeline(Stage("a", "plan", "ticket-planner")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
