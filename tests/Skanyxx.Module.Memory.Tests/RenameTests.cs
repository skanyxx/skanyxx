using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D038: a person renames a card's key where they may write (the upsert rule), against the version they read, to a key
/// free in the scope. The row keeps its id, so lift links survive. Agents cannot rename. Org: ana is in team billing
/// (department finance); bob is in payroll (finance); the supervisor and the owner are in nothing.
/// </summary>
public sealed class RenameTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        App.Org.Join("ana", "billing", "finance");
        App.Org.Join("bob", "payroll", "finance");
    }

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, string scope, string key, string newKey, int version = 1) =>
        client.PostAsJsonAsync($"/api/memory/cards/{scope}/{key}/rename", new { newKey, version });

    [Fact]
    public async Task Owner_RenamesTheirCard_OldKeyIsGone_VersionBumps_WhoStays()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-windw", body: "details", source: "chat://1");

        var renamed = await RenameAsync(ana, "personal:ana", "refund-windw", "refund-window");

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var card = await renamed.CardAsync();
        Assert.Equal(("refund-window", 2, "ana", "details", "chat://1"), (card.Key, card.Version, card.Who, card.Body, card.Source));
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/api/memory/cards/personal:ana/refund-windw", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/memory/cards/personal:ana/refund-window", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(["refund-window"], (await ana.SearchAsync("refund window")).Select(h => h.Key)); // the search vector follows the key
        Assert.Contains("Card personal:ana/refund-windw renamed to refund-window (version 2) by ana", App.Logs);
        Assert.Empty(App.Errors);
    }

    /// <summary>Who may rename = who may write the scope (CanUpsertAsync), for every level.</summary>
    [Theory]
    [InlineData("personal:ana", "ana", "ana", HttpStatusCode.OK)]
    [InlineData("personal:ana", "ana", "bob", HttpStatusCode.Forbidden)]          // someone else's personal
    [InlineData("personal:ana", "ana", MemoryApp.Supervisor, HttpStatusCode.Forbidden)]
    [InlineData("team:billing", "ana", "ana", HttpStatusCode.OK)]                 // member
    [InlineData("team:billing", "ana", "bob", HttpStatusCode.Forbidden)]          // same department, other team
    [InlineData("team:billing", "ana", MemoryApp.Supervisor, HttpStatusCode.Forbidden)] // oversight reads, never writes
    [InlineData("team:billing", "ana", MemoryApp.Owner, HttpStatusCode.Forbidden)]
    [InlineData("department:finance", "ana", "bob", HttpStatusCode.OK)]           // member through payroll
    [InlineData("department:finance", "ana", "carol", HttpStatusCode.Forbidden)]
    [InlineData("company", MemoryApp.Supervisor, MemoryApp.Supervisor, HttpStatusCode.OK)]
    [InlineData("company", MemoryApp.Supervisor, MemoryApp.Owner, HttpStatusCode.OK)]
    [InlineData("company", MemoryApp.Supervisor, "ana", HttpStatusCode.Forbidden)]
    public async Task RenameNeedsWriteRightsOnTheScope(string scope, string author, string renamer, HttpStatusCode expected)
    {
        Assert.Equal(HttpStatusCode.Created, (await Client(author).PutCardAsync(scope, "refund-window")).StatusCode);

        var response = await RenameAsync(Client(renamer), scope, "refund-window", "refunds");

        Assert.Equal(expected, response.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(expected == HttpStatusCode.OK ? "refunds" : "refund-window", Assert.Single(db.Cards).Key);
    }

    [Fact]
    public async Task Refusal_TellsAPersonWhoMayWrite()
    {
        await App.Client("ana").PutCardAsync("team:billing", "refund-window");

        var response = await RenameAsync(App.Client("bob"), "team:billing", "refund-window", "refunds");

        Assert.Contains("Only members of team:billing may write there.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaleVersion_Conflicts_WithTheCurrentCard()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");
        await ana.PutCardAsync("personal:ana", "refund-window", version: 1, what: "Changed");

        var response = await RenameAsync(ana, "personal:ana", "refund-window", "refunds", version: 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("stale", body.GetProperty("reason").GetString());
        var current = body.GetProperty("current");
        Assert.Equal(2, current.GetProperty("version").GetInt32());
        Assert.Equal("refund-window", current.GetProperty("key").GetString());
        await using var db = Postgres.CreateDbContext();
        Assert.Equal("refund-window", Assert.Single(db.Cards).Key);
    }

    [Fact]
    public async Task TakenKey_Conflicts_WithTheCardAlreadyThere_AndChangesNothing()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");
        await ana.PutCardAsync("personal:ana", "refunds", what: "The other one");

        var response = await RenameAsync(ana, "personal:ana", "refund-window", "refunds");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("The other one", body.GetProperty("current").GetProperty("what").GetString());
        Assert.Equal("taken", body.GetProperty("reason").GetString());
        Assert.Empty(App.Errors); // checked before the UPDATE, so no failed statement is logged
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(["refund-window", "refunds"], db.Cards.Select(c => c.Key).Order());
    }

    [Fact]
    public async Task SameKeyInAnotherScope_IsNotTaken()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("team:billing", "refunds");
        await ana.PutCardAsync("personal:ana", "refund-window");

        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(ana, "personal:ana", "refund-window", "refunds")).StatusCode);
    }

    /// <summary>Lift links are by id: renaming the source or the copy keeps every link.</summary>
    [Fact]
    public async Task LiftedFrom_SurvivesRenamingTheSourceAndTheCopy()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");
        var copy = await (await ana.LiftAsync("personal:ana", "refund-window", "team:billing")).CardAsync();
        var second = await (await ana.LiftAsync("team:billing", "refund-window", "department:finance")).CardAsync();

        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(ana, "personal:ana", "refund-window", "my-refunds")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(ana, "team:billing", "refund-window", "billing-refunds")).StatusCode);

        await using var db = Postgres.CreateDbContext();
        var original = db.Cards.Single(c => c.Scope == "personal:ana");
        var teamCopy = db.Cards.Single(c => c.Scope == "team:billing");
        var departmentCopy = db.Cards.Single(c => c.Scope == "department:finance");
        Assert.Equal(("my-refunds", original.Id), (original.Key, copy.LiftedFromId!.Value));
        Assert.Equal(("billing-refunds", original.Id), (teamCopy.Key, teamCopy.LiftedFromId!.Value));
        Assert.Equal(("refund-window", teamCopy.Id), (departmentCopy.Key, second.LiftedFromId!.Value));
        Assert.Equal(teamCopy.Id, departmentCopy.LiftedFromId);
    }

    [Theory]
    [InlineData("Bad Key")]
    [InlineData("refund-window")] // the current key
    [InlineData("")]
    public async Task InvalidNewKey_IsAValidationError(string newKey)
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var response = await RenameAsync(ana, "personal:ana", "refund-window", newKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty("newKey", out _));
    }

    [Fact]
    public async Task VersionZero_IsAValidationError()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        Assert.Equal(HttpStatusCode.BadRequest, (await RenameAsync(ana, "personal:ana", "refund-window", "refunds", version: 0)).StatusCode);
    }

    [Fact]
    public async Task MissingCard_IsNotFound()
    {
        var response = await RenameAsync(App.Client("ana"), "personal:ana", "nothing-here", "something");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenameAsync(App.Client(), "company", "refund-window", "refunds")).StatusCode);
    }

    /// <summary>
    /// Agents propose keys and never rename them: the MCP tools have no rename, and an agent secret is not a sign-in on
    /// the REST route.
    /// </summary>
    [Fact]
    public async Task AnAgentCannotRename()
    {
        await App.SupervisorClient().PutCardAsync("company", "refund-window");
        var secret = await App.IssueSecretAsync("seed");
        await using var mcp = await App.McpAsync("seed");
        var agent = App.Client();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", secret);

        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var response = await RenameAsync(agent, "company", "refund-window", "refunds");

        Assert.DoesNotContain(tools, t => t.Name.Contains("rename", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal("refund-window", Assert.Single(db.Cards).Key);
    }

    [Fact]
    public async Task QueryString_CannotRetargetTheRename()
    {
        var ana = App.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window");

        var response = await ana.PostAsJsonAsync("/api/memory/cards/personal:ana/refund-window/rename?scope=company&newKey=x&version=9",
            new { newKey = "refunds", version = 1 }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = await response.CardAsync();
        Assert.Equal(("personal:ana", "refunds"), (card.Scope, card.Key));
    }

    private HttpClient Client(string user) => user switch
    {
        MemoryApp.Supervisor => App.SupervisorClient(),
        MemoryApp.Owner => App.OwnerClient(),
        _ => App.Client(user)
    };
}
