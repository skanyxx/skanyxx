using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Features.Cards;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D4 of the task (D090): an agent reaches a team or department only through a grant the owner set (D091). Acting for a
/// user who is a member gives it that user's personal scope, never the user's teams.
/// </summary>
public sealed class OrgAgentTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        App.Org.Join(Users.Ana, "billing", "finance");
        await App.Client(Users.Ana).PutCardAsync("team:billing", "team-refund");
        await App.Client(Users.Ana).PutCardAsync("department:finance", "department-refund");
    }

    [Fact]
    public async Task ActsForAMember_WithDefaultGrants_SeesAndWritesNoTeamOrDepartment()
    {
        await App.Client(Users.Ana).PutCardAsync($"personal:{Users.Ana}", "ana-refund");
        await using var client = await App.McpAsync("seed", userId: Users.Ana);

        var found = Text(await client.CallToolAsync("memory_search", Query("refund"), cancellationToken: TestContext.Current.CancellationToken));
        var team = await client.CallToolAsync("memory_upsert", Upsert("agent-card", "team:billing"), cancellationToken: TestContext.Current.CancellationToken);
        var department = await client.CallToolAsync("memory_upsert", Upsert("agent-card", "department:finance"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("ana-refund", found);
        Assert.DoesNotContain("team-refund", found);
        Assert.DoesNotContain("department-refund", found);
        Assert.True(team.IsError);
        Assert.True(department.IsError);
    }

    [Fact]
    public async Task WithATeamGrant_SearchesAndWritesThatTeam_WhoeverItActsFor()
    {
        await App.OwnerClient().SetGrantsAsync("billing-bot", new { scope = "team:billing", canSearch = true, canUpsert = true });
        await using var client = await App.McpWithSecretAsync(await App.IssueSecretAsync("billing-bot", byOwner: true));

        var found = Text(await client.CallToolAsync("memory_search", Query("refund"), cancellationToken: TestContext.Current.CancellationToken));
        var written = await client.CallToolAsync("memory_upsert", Upsert("agent-card", "team:billing"), cancellationToken: TestContext.Current.CancellationToken);
        var department = await client.CallToolAsync("memory_upsert", Upsert("agent-card", "department:finance"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("team-refund", found);
        Assert.DoesNotContain("department-refund", found);
        Assert.NotEqual(true, written.IsError);
        Assert.True(department.IsError);
    }

    [Fact]
    public async Task UpsertOnlyTeamGrant_ConflictDoesNotRevealTheCard()
    {
        await App.OwnerClient().SetGrantsAsync("blind", new { scope = "team:billing", canSearch = false, canUpsert = true });
        await using var scope = App.Services.CreateAsyncScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new UpsertCardCommand(
            new MemoryCaller(null, "blind"), "team:billing", "team-refund", 0, "fact", "w", "y"), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.Conflict, outcome.Status);
        Assert.Null(outcome.Value);
    }

    private static Dictionary<string, object?> Query(string query) => new() { ["query"] = query };

    private static Dictionary<string, object?> Upsert(string key, string scope) => new()
    {
        ["key"] = key, ["type"] = "decision", ["what"] = "We refund within 14 days", ["why"] = "Finance policy X", ["version"] = 0, ["scope"] = scope
    };

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
        (result.StructuredContent is { } s ? JsonSerializer.Serialize(s) : "");
}
