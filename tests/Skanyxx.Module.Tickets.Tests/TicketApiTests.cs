using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Tests.Infrastructure;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class TicketApiTests(PostgresFixture postgres) : TicketsTestBase(postgres)
{
    [Fact]
    public async Task List_ReturnsTheSourcesTickets()
    {
        var tickets = await App.Client().GetFromJsonAsync<List<Ticket>>("/api/tickets/issues", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["SDB-1", "SDB-2", "SDB-3"], tickets!.Select(t => t.Key));
    }

    [Theory]
    [InlineData("Dana Levi", new[] { "SDB-1", "SDB-3" })]
    [InlineData("unassigned", new[] { "SDB-2" })]
    [InlineData("Nobody", new string[0])]
    public async Task List_FiltersByAssignee(string assignee, string[] expected)
    {
        var tickets = await App.Client().GetFromJsonAsync<List<Ticket>>(
            $"/api/tickets/issues?assignee={Uri.EscapeDataString(assignee)}", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, tickets!.Select(t => t.Key));
    }

    [Fact]
    public async Task EmptyAssignee_IsRejected_NotTreatedAsNoFilter()
    {
        var response = await App.Client().GetAsync("/api/tickets/issues?assignee=%20", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assignees_AreCounted_WithUnassignedExplicit()
    {
        var counts = await App.Client().GetFromJsonAsync<List<AssigneeCount>>("/api/tickets/issues/assignees", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(new AssigneeCount("unassigned", 1), counts!);
        Assert.Contains(new AssigneeCount("Dana Levi", 1), counts!);
    }

    [Fact]
    public async Task Get_ReturnsOneTicket_Or404()
    {
        var found = await App.Client().GetFromJsonAsync<Ticket>("/api/tickets/issues/SDB-2", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        var missing = await App.Client().GetAsync("/api/tickets/issues/SDB-99", TestContext.Current.CancellationToken);

        Assert.Equal("Export cards as CSV", found!.Title);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("/api/tickets/issues")]
    [InlineData("/api/tickets/pipelines")]
    [InlineData("/api/tickets/runs")]
    public async Task EveryEndpoint_RequiresAUser(string path)
    {
        var anonymous = await App.Client(userId: null).GetAsync(path, TestContext.Current.CancellationToken);
        var invalid = App.Client(userId: "Ana Smith");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await invalid.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Cors_IsOff_ForTicketsEndpoints()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tickets/issues");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await App.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ListRuns_NewestFirst_FilteredByTicket()
    {
        await App.StartRunAsync("SDB-1");
        await App.StartRunAsync("SDB-2");

        var all = await App.Client().GetFromJsonAsync<List<RunSummaryDto>>("/api/tickets/runs", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);
        var one = await App.Client().GetFromJsonAsync<List<RunSummaryDto>>("/api/tickets/runs?ticketKey=SDB-1", TicketsApp.Json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["SDB-2", "SDB-1"], all!.Select(r => r.TicketKey));
        Assert.Equal(["SDB-1"], one!.Select(r => r.TicketKey));
    }
}
