using System.Text.RegularExpressions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

/// <summary>
/// FTS read path (Dapper): any query word matches, best rank first (D013, D037). Agents get published cards only; the
/// library also lists the other statuses outside <c>company</c> (D048: company is its published cards).
/// </summary>
public sealed partial class CardSearch(MemoryDbContext db)
{
    public const int MaxTerms = 32;
    public const int MaxWordLength = 64;

    // An agent's natural-language question rarely contains every word of a card, so each word becomes
    // its own plainto_tsquery (stemmed, stop words dropped, punctuation neutralised) and they are OR-ed;
    // ts_rank then orders by how many, and which, matched. Words are bound as parameters, never spliced.
    private const string Sql = """
        WITH q AS (SELECT {0} AS query)
        SELECT c.scope AS Scope, c.key AS Key, c.version AS Version, c.type AS Type,
               c.what AS What, c.why AS Why, c.who AS Who, c.updated_at AS UpdatedAt
        FROM memory_cards c, q
        WHERE c.status = 'published' AND (c.scope = ANY(@Scopes) OR (@AllOrg AND c.scope ~ '^(team|department):'))
          AND c.search @@ q.query
        ORDER BY ts_rank(c.search, q.query) DESC, c.updated_at DESC
        LIMIT @Limit
        """;

    // Two statements rather than one with "query IS NULL OR search @@ query": that OR keeps the planner off the GIN
    // index, so every text search would scan the table. Same columns, same scopes, same company rule (D048).
    private const string LibraryColumns = """
        SELECT c.scope AS Scope, c.key AS Key, c.version AS Version, c.type AS Type, c.what AS What, c.why AS Why,
               c.who AS Who, c.updated_at AS UpdatedAt, c.status AS Status
        """;

    private const string LibraryFilter = """
        (c.scope = ANY(@Scopes) OR (@AllOrg AND c.scope ~ '^(team|department):'))
          AND (c.status = 'published' OR c.scope <> 'company')
        """;

    private const string LibraryTextSql = $$"""
        WITH q AS (SELECT {0} AS query)
        {{LibraryColumns}}
        FROM memory_cards c, q
        WHERE {{LibraryFilter}}
          AND c.search @@ q.query
        ORDER BY ts_rank(c.search, q.query) DESC, c.updated_at DESC, c.scope, c.key
        LIMIT @Limit
        """;

    private const string LibraryListSql = $"""
        {LibraryColumns}
        FROM memory_cards c
        WHERE {LibraryFilter}
        ORDER BY c.updated_at DESC, c.scope, c.key
        LIMIT @Limit
        """;

    /// <summary>
    /// Each OR-ed term costs an index probe per lexeme, so the query is split the way Postgres would split it
    /// (on anything not a letter or digit: 'follow-up' → 'follow', 'up') and the cap counts those words.
    /// Words longer than any real one are dropped. The LAST words are kept: a question's subject usually comes
    /// at the end ("…how long is the refund window").
    /// </summary>
    internal static string[] Terms(string query) =>
        WordPattern().Matches(query)
            .Select(m => m.Value)
            .Where(word => word.Length <= MaxWordLength)
            .Reverse()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToArray();

    public async Task<IReadOnlyList<CardHit>> SearchAsync(string query, SearchScopes scopes, int limit, CancellationToken ct)
    {
        var terms = Terms(query);
        if ((scopes.Exact.Count == 0 && !scopes.AllTeamsAndDepartments) || terms.Length == 0)
            return [];

        var parameters = new DynamicParameters(new { Scopes = scopes.Exact.ToArray(), AllOrg = scopes.AllTeamsAndDepartments, Limit = limit });
        var tsquery = TsQuery(terms, parameters);

        var rows = await db.Database.GetDbConnection().QueryAsync<CardHit>(
            new CommandDefinition(string.Format(Sql, tsquery), parameters, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>The library list: <paramref name="text"/> null or blank lists everything in <paramref name="scopes"/>, newest first.</summary>
    public async Task<IReadOnlyList<LibraryEntry>> LibraryAsync(string? text, SearchScopes scopes, int limit, CancellationToken ct)
    {
        if (scopes.Exact.Count == 0 && !scopes.AllTeamsAndDepartments)
            return [];
        var command = LibraryCommand(text, scopes, limit);
        if (command is not { } sql)
            return [];

        var rows = await db.Database.GetDbConnection().QueryAsync<LibraryEntry>(
            new CommandDefinition(sql.Text, sql.Parameters, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>The statement <see cref="LibraryAsync"/> runs; null when the text has no searchable word.</summary>
    internal static (string Text, DynamicParameters Parameters)? LibraryCommand(string? text, SearchScopes scopes, int limit)
    {
        var parameters = new DynamicParameters(new { Scopes = scopes.Exact.ToArray(), AllOrg = scopes.AllTeamsAndDepartments, Limit = limit });
        if (string.IsNullOrWhiteSpace(text))
            return (LibraryListSql, parameters);

        var terms = Terms(text);
        return terms.Length == 0 ? null : (string.Format(LibraryTextSql, TsQuery(terms, parameters)), parameters);
    }

    /// <summary>Team and department scopes that hold any card: what oversight can browse, org object or not.</summary>
    public async Task<IReadOnlyList<string>> OrgScopesWithCardsAsync(CancellationToken ct) =>
        (await db.Database.GetDbConnection().QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT scope FROM memory_cards WHERE scope ~ '^(team|department):' ORDER BY scope", cancellationToken: ct))).AsList();

    private static string TsQuery(string[] terms, DynamicParameters parameters)
    {
        for (var i = 0; i < terms.Length; i++)
            parameters.Add($"t{i}", terms[i]);
        return string.Join(" || ", terms.Select((_, i) => $"plainto_tsquery('english', @t{i})"));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
