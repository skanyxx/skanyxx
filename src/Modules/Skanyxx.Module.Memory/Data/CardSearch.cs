using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Coordination;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

/// <summary>
/// FTS read path (Dapper): any query word matches, best rank first (D013, D037). A ticket key in the query (or given
/// with it) also matches cards carrying that ticket, and those rank first: that is how work on SDB-64 finds what was
/// learnt on SDB-58. Agents get published cards only; the library also lists the other statuses outside <c>company</c>
/// (D048: company is its published cards).
/// </summary>
public sealed partial class CardSearch(MemoryDbContext db, SearchCache cache)
{
    public const int MaxTerms = 32;
    public const int MaxWordLength = 64;
    public const int MaxTickets = 10;

    // An agent's natural-language question rarely contains every word of a card, so each word becomes
    // its own plainto_tsquery (stemmed, stop words dropped, punctuation neutralised) and they are OR-ed;
    // ts_rank then orders by how many, and which, matched. Words are bound as parameters, never spliced.
    // Each OR arm has its own index (search, tickets, keywords: GIN), so the planner can BitmapOr them.
    private const string Sql = """
        WITH q AS (SELECT {0} AS query)
        SELECT c.scope AS Scope, c.key AS Key, c.version AS Version, c.type AS Type,
               c.what AS What, c.why AS Why, c.who AS Who, c.updated_at AS UpdatedAt, c.tickets AS Tickets
        FROM memory_cards c, q
        WHERE c.status = 'published' AND (c.scope = ANY(@Scopes) OR (@AllOrg AND c.scope ~ '^(team|department):'))
          AND (c.search @@ q.query OR c.tickets && @Tickets OR c.keywords && @Words)
        ORDER BY (c.tickets && @Tickets) DESC, ts_rank(c.search, q.query) DESC NULLS LAST, c.updated_at DESC
        LIMIT @Limit
        """;

    // Two statements rather than one with "query IS NULL OR search @@ query": that OR keeps the planner off the GIN
    // index, so every text search would scan the table. Same columns, same scopes, same company rule (D048).
    private const string LibraryColumns = """
        SELECT c.scope AS Scope, c.key AS Key, c.version AS Version, c.type AS Type, c.what AS What, c.why AS Why,
               c.who AS Who, c.updated_at AS UpdatedAt, c.status AS Status, c.tickets AS Tickets, c.origin AS Origin
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
          AND c.search @@ q.query{1}
        ORDER BY {3}
        LIMIT @Limit{2}
        """;

    private const string LibraryListSql = $$"""
        {{LibraryColumns}}
        FROM memory_cards c
        WHERE {{LibraryFilter}}{0}
        ORDER BY {3}
        LIMIT @Limit{2}
        """;

    // D355: the facet counts, one row per type, with every filter but the type (the total is their sum, or the
    // filtered type's row). Same predicates as the page, so the count can never disagree with what paging reaches.
    private const string LibraryTextCountSql = $$"""
        WITH q AS (SELECT {0} AS query)
        SELECT c.type AS Type, count(*)::int AS Count
        FROM memory_cards c, q
        WHERE {{LibraryFilter}}
          AND c.search @@ q.query{1}
        GROUP BY c.type
        """;

    private const string LibraryListCountSql = $$"""
        SELECT c.type AS Type, count(*)::int AS Count
        FROM memory_cards c
        WHERE {{LibraryFilter}}{0}
        GROUP BY c.type
        """;

    // Every order ends in (scope, key), unique, so pages never overlap or skip a card between equal values.
    private const string Newest = "c.updated_at DESC, c.scope, c.key";

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

    /// <param name="tickets">Ticket keys known to be relevant, besides any <paramref name="query"/> mentions.</param>
    public async Task<IReadOnlyList<CardHit>> SearchAsync(
        string query, SearchScopes scopes, int limit, CancellationToken ct, IReadOnlyList<string>? tickets = null)
    {
        var terms = Terms(query);
        string[] keys = [.. CardTickets.FindIn(query, MaxTickets).Concat(tickets ?? []).Distinct().Take(MaxTickets)];
        if ((scopes.Exact.Count == 0 && !scopes.AllTeamsAndDepartments) || (terms.Length == 0 && keys.Length == 0))
            return [];

        // Oversight's scope set is "every team and department", which no key can name; it is never cached.
        return scopes.AllTeamsAndDepartments
            ? await QueryAsync()
            : await cache.GetOrSearchAsync(scopes.Exact, string.Join(' ', terms), keys, limit, QueryAsync);

        async Task<IReadOnlyList<CardHit>> QueryAsync()
        {
            var parameters = new DynamicParameters(new
            {
                Scopes = scopes.Exact.ToArray(), AllOrg = scopes.AllTeamsAndDepartments, Limit = limit, Tickets = keys,
                Words = terms.Select(t => t.ToLowerInvariant()).ToArray()
            });
            var tsquery = terms.Length == 0 ? "NULL::tsquery" : TsQuery(terms, parameters);
            var rows = await db.Database.GetDbConnection().QueryAsync<CardRow>(
                new CommandDefinition(string.Format(Sql, tsquery), parameters, cancellationToken: ct));
            return rows.Select(r => r.ToHit()).ToList();
        }
    }

    /// <summary>The library list: <paramref name="text"/> null or blank lists everything in <paramref name="scopes"/>, newest first.</summary>
    public async Task<IReadOnlyList<LibraryEntry>> LibraryAsync(
        string? text, SearchScopes scopes, int limit, CancellationToken ct, LibraryFilter? filter = null)
    {
        if (scopes.Exact.Count == 0 && !scopes.AllTeamsAndDepartments)
            return [];
        var command = LibraryCommand(text, scopes, limit, filter);
        if (command is not { } sql)
            return [];

        var rows = await db.Database.GetDbConnection().QueryAsync<CardRow>(
            new CommandDefinition(sql.Text, sql.Parameters, cancellationToken: ct));
        return rows.Select(r => r.ToEntry()).ToList();
    }

    /// <summary>
    /// The statement <see cref="LibraryAsync"/> runs; null when the text has no searchable word. Only the filters that are
    /// set become predicates, so an unfiltered list keeps the plan it had.
    /// </summary>
    internal static (string Text, DynamicParameters Parameters)? LibraryCommand(
        string? text, SearchScopes scopes, int limit, LibraryFilter? filter = null)
    {
        filter ??= Core.Platform.Memory.LibraryFilter.None;
        var parameters = new DynamicParameters(new
        {
            Scopes = scopes.Exact.ToArray(), AllOrg = scopes.AllTeamsAndDepartments, Limit = limit, filter.Offset
        });
        var predicates = Filters(filter, parameters, withType: true);
        // Only a later page says OFFSET, so the first page keeps the plan it always had.
        var paging = filter.Offset > 0 ? " OFFSET @Offset" : "";
        if (string.IsNullOrWhiteSpace(text))
            return (string.Format(LibraryListSql, predicates, null, paging, Order(filter.Sort, text: false)), parameters);

        var terms = Terms(text);
        return terms.Length == 0
            ? null
            : (string.Format(LibraryTextSql, TsQuery(terms, parameters), predicates, paging, Order(filter.Sort, text: true)), parameters);
    }

    /// <summary>The facet-count statement for the same text and filters (type left out); null when the text has no word.</summary>
    internal static (string Text, DynamicParameters Parameters)? LibraryCountCommand(string? text, SearchScopes scopes, LibraryFilter? filter = null)
    {
        filter ??= Core.Platform.Memory.LibraryFilter.None;
        var parameters = new DynamicParameters(new { Scopes = scopes.Exact.ToArray(), AllOrg = scopes.AllTeamsAndDepartments });
        var predicates = Filters(filter, parameters, withType: false);
        if (string.IsNullOrWhiteSpace(text))
            return (string.Format(LibraryListCountSql, predicates), parameters);
        var terms = Terms(text);
        return terms.Length == 0 ? null : (string.Format(LibraryTextCountSql, TsQuery(terms, parameters), predicates), parameters);
    }

    /// <summary>Matching cards per type (D355), over the same scopes, text and filters as <see cref="LibraryAsync"/> minus the type.</summary>
    public async Task<IReadOnlyDictionary<string, int>> LibraryCountsAsync(
        string? text, SearchScopes scopes, CancellationToken ct, LibraryFilter? filter = null)
    {
        if ((scopes.Exact.Count == 0 && !scopes.AllTeamsAndDepartments) || LibraryCountCommand(text, scopes, filter) is not { } sql)
            return new Dictionary<string, int>();
        var rows = await db.Database.GetDbConnection().QueryAsync<(string Type, int Count)>(
            new CommandDefinition(sql.Text, sql.Parameters, cancellationToken: ct));
        return rows.ToDictionary(r => r.Type, r => r.Count);
    }

    private static string Order(string? sort, bool text) => sort switch
    {
        "oldest" => "c.updated_at ASC, c.scope, c.key",
        "what" => "c.what, c.scope, c.key",
        "newest" => Newest,
        _ when text => "ts_rank(c.search, q.query) DESC, " + Newest,
        _ => Newest
    };

    private static string Filters(LibraryFilter filter, DynamicParameters parameters, bool withType)
    {
        var sql = new StringBuilder();
        Add(filter.Ticket, "c.tickets @> ARRAY[@Ticket]::text[]", "Ticket");
        Add(filter.Keyword, "c.keywords @> ARRAY[@Keyword]::text[]", "Keyword");
        if (withType)
            Add(filter.Type, "c.type = @Type", "Type");
        Add(filter.Origin, "c.origin = @Origin", "Origin");
        Add(filter.Agent, "c.agent_id = @Agent", "Agent");
        Add(filter.Owner, "c.who = @Owner", "Owner");
        Add(filter.From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), "c.updated_at >= @From", "From");
        // To is an inclusive day: everything before the next midnight.
        Add(filter.To?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), "c.updated_at < @To", "To");
        return sql.ToString();

        void Add(object? value, string predicate, string name)
        {
            if (value is null or "")
                return;
            parameters.Add(name, value);
            sql.Append("\n  AND ").Append(predicate);
        }
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
