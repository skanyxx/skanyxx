using System.Net;
using System.Net.Http.Json;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Tests.Infrastructure;
using static Skanyxx.Module.Tickets.Tests.Infrastructure.Stages;

namespace Skanyxx.Module.Tickets.Tests;

/// <summary>Identity is the signed-in principal only; the old X-User-Id header neither signs in nor overrides it.</summary>
public sealed class AuthenticationTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    private const string AnyRun = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/api/tickets/issues" },
        { "GET", "/api/tickets/issues/assignees" },
        { "GET", "/api/tickets/issues/SDB-1" },
        { "GET", "/api/tickets/pipelines" },
        { "GET", "/api/tickets/pipelines/ticket-fix" },
        { "PUT", "/api/tickets/pipelines/mine" },
        { "DELETE", "/api/tickets/pipelines/ticket-fix" },
        { "GET", "/api/tickets/runs" },
        { "POST", "/api/tickets/runs" },
        { "GET", $"/api/tickets/runs/{AnyRun}" },
        { "GET", $"/api/tickets/runs/{AnyRun}/report" },
        { "GET", $"/api/tickets/runs/{AnyRun}/dataset" },
        { "POST", $"/api/tickets/runs/{AnyRun}/cancel" },
        { "POST", $"/api/tickets/runs/{AnyRun}/decision" }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task UserIdHeaderAlone_IsUnauthorized(string method, string path)
    {
        var client = App.Client(userId: null);
        client.DefaultRequestHeaders.Add("X-User-Id", TicketsApp.Supervisor);
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PUT" or "POST")
            request.Content = JsonContent.Create(new { ticketKey = "SDB-1", pipelineId = "ticket-fix", decision = "approve", name = "n", stages = Array.Empty<object>() });

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty((await App.Client().GetFromJsonAsync<List<RunSummaryDto>>("/api/tickets/runs", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task CreatedByAndDecidedBy_ComeFromTheSignedInUser_NotTheHeader()
    {
        var ana = App.Client();
        ana.DefaultRequestHeaders.Add("X-User-Id", "bob");

        var started = await ana.PostAsJsonAsync("/api/tickets/runs", new { ticketKey = "SDB-1", pipelineId = "ticket-fix" }, cancellationToken: TestContext.Current.CancellationToken);
        var run = (await started.Content.ReadFromJsonAsync<RunDto>(TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken))!;
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);
        var decided = await ana.PostAsJsonAsync($"/api/tickets/runs/{run.Id}/decision", new { decision = "approve" }, cancellationToken: TestContext.Current.CancellationToken);
        var done = await App.WaitForAsync(run.Id, RunState.Succeeded);

        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal(TicketsApp.User, run.CreatedBy);
        Assert.Equal(TicketsApp.User, done.StageRuns[^1].DecidedBy);
    }

    [Fact]
    public async Task AnotherUser_ClaimingTheCreatorInTheHeader_CannotCancel()
    {
        var run = await App.StartRunAsync();
        var bob = App.Client("bob");
        bob.DefaultRequestHeaders.Add("X-User-Id", TicketsApp.User);

        var response = await bob.PostAsync($"/api/tickets/runs/{run.Id}/cancel", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(SkanyxxRoles.Owner, HttpStatusCode.Created)]
    [InlineData(SkanyxxRoles.Supervisor, HttpStatusCode.Created)]
    [InlineData(SkanyxxRoles.Employee, HttpStatusCode.Forbidden)]
    [InlineData(SkanyxxRoles.Builder, HttpStatusCode.Forbidden)]
    public async Task Role_DecidesSupervisorRights(string role, HttpStatusCode expected)
    {
        var run = await App.StartRunAsync();
        await App.WaitForAsync(run.Id, RunState.AwaitingHuman);
        var olga = App.ClientAs("olga", role);

        var save = await olga.PutAsJsonAsync("/api/tickets/pipelines/mine", Pipeline(Stage("plan", "plan", "ticket-planner")), cancellationToken: TestContext.Current.CancellationToken);
        var cancel = await olga.PostAsync($"/api/tickets/runs/{run.Id}/cancel", null, TestContext.Current.CancellationToken);

        Assert.Equal(expected, save.StatusCode);
        Assert.Equal(expected == HttpStatusCode.Created ? HttpStatusCode.OK : HttpStatusCode.Forbidden, cancel.StatusCode);
    }

    [Fact]
    public async Task SupervisorByName_WithoutRole_IsNotASupervisor()
    {
        var response = await App.ClientAs(TicketsApp.Supervisor).PutAsJsonAsync("/api/tickets/pipelines/mine",
            Pipeline(Stage("plan", "plan", "ticket-planner")), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
