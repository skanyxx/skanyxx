using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// CR L2: the library runs one statement with text and another without. With text the planner can use the GIN index on
/// <c>search</c> in every plan; a single statement with "query IS NULL OR search @@ query" can only when the planner
/// sees the words as constants and folds the OR away — a generic (cached, prepared) plan cannot, and scans the table.
/// Without text there is no FTS predicate at all. Behaviour is LibraryTests'; this pins the shape.
/// </summary>
public sealed partial class LibrarySearchPlanTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private static readonly SearchScopes Oversight = new(["company", "personal:boss"], AllTeamsAndDepartments: true);

    [Fact]
    public async Task WithText_TheSearchIndexIsUsable()
    {
        var plan = await PlanAsync("refund window");

        Assert.Contains("IX_memory_cards_search", plan);
    }

    [Fact]
    public async Task WithoutText_ThereIsNoTextPredicate()
    {
        var (sql, _) = CardSearch.LibraryCommand(null, Oversight, 50)!.Value;
        var blank = CardSearch.LibraryCommand("   ", Oversight, 50)!.Value;

        Assert.DoesNotContain("@@", sql);
        Assert.DoesNotContain("tsquery", sql);
        Assert.Equal(sql, blank.Text);
        Assert.Null(CardSearch.LibraryCommand("!!! ???", Oversight, 50)); // text with no word finds nothing
    }

    /// <summary>
    /// The statement prepared and planned generically (as a cached plan is), with sequential scans priced out: whatever
    /// index it can use for any values, the planner shows.
    /// </summary>
    private async Task<string> PlanAsync(string text)
    {
        var (sql, parameters) = CardSearch.LibraryCommand(text, Oversight, 50)!.Value;
        var terms = parameters.ParameterNames.Count(n => n.StartsWith('t'));
        var prepared = Parameter().Replace(sql, m => m.Groups[1].Value switch
        {
            "Scopes" => "$1",
            "AllOrg" => "$2",
            "Limit" => "$3",
            var term => $"${4 + int.Parse(term[1..])}"
        });
        var types = string.Join(", ", ["text[]", "boolean", "int", .. Enumerable.Repeat("text", terms)]);
        // EXECUTE's arguments are literals: a utility statement takes no bound parameters. Test-owned values only.
        var arguments = string.Join(", ", [
            $"ARRAY[{string.Join(", ", Oversight.Exact.Select(Quote))}]", Oversight.AllTeamsAndDepartments ? "true" : "false", "50",
            .. CardSearch.Terms(text).Select(Quote)]);

        await using var connection = new NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SET enable_seqscan = off; SET plan_cache_mode = force_generic_plan");
        await connection.ExecuteAsync($"PREPARE library({types}) AS {prepared}");
        return string.Join("\n", await connection.QueryAsync<string>($"EXPLAIN EXECUTE library({arguments})"));
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    [GeneratedRegex(@"@(Scopes|AllOrg|Limit|t\d+)\b")]
    private static partial Regex Parameter();
}
