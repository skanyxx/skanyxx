using Dapper;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Extraction;

namespace Skanyxx.Module.ChatContext.Data;

/// <summary>
/// The index's read path (Dapper over the GIN indexes). Every statement carries <see cref="ReadableFilter"/>, so no query
/// can return a session the reader may not read (D237). Text search is memory's: each word its own plainto_tsquery,
/// OR-ed, ranked by ts_rank; words are parameters, never spliced.
/// </summary>
internal sealed class SessionSearch(ChatContextDbContext db)
{
    public const int MaxTerms = 24;

    private const string Columns = """
        s.session_id AS SessionId, s.title AS Title, s.agent_ref AS AgentRef, s.scope AS Scope, s.keywords AS Keywords,
        s.categories AS Categories, s.ticket_keys AS TicketKeys, s.outcome AS Outcome, s.turns AS Turns,
        s.created_at AS CreatedAt, s.updated_at AS UpdatedAt, (s.user_id = @Me) AS Mine
        """;

    private const string ReadableFilter = "(s.user_id = @Me OR s.scope = ANY(@Scopes) OR (@AllOrg AND s.scope ~ '^(team|department):'))";

    /// <summary>The words a search runs on: what the extractor would index, last ones kept (a question's subject comes late).</summary>
    internal static string[] Terms(string text) =>
        [.. DeterministicExtractor.Words(text).Reverse().Distinct().Take(MaxTerms)];

    public async Task<IReadOnlyList<SessionSummary>> SearchAsync(Readable readable, SessionFilter filter, int limit, CancellationToken ct)
    {
        var parameters = Parameters(readable, limit);
        List<string> where = [ReadableFilter];
        if (filter.Mine)
            where.Add("s.user_id = @Me");
        if (filter.Ticket is { } ticket)
        {
            parameters.Add("Ticket", new[] { ticket });
            where.Add("s.ticket_keys @> @Ticket");
        }
        if (filter.Category is { } category)
        {
            parameters.Add("Category", new[] { category });
            where.Add("s.categories @> @Category");
        }
        if (filter.Agent is { } agent)
        {
            parameters.Add("Agent", agent);
            where.Add("s.agent_ref = @Agent");
        }

        var terms = string.IsNullOrWhiteSpace(filter.Text) ? [] : Terms(filter.Text);
        if (!string.IsNullOrWhiteSpace(filter.Text) && terms.Length == 0)
            return [];
        string sql;
        if (terms.Length == 0)
            sql = $"SELECT {Columns} FROM chat_sessions s WHERE {string.Join(" AND ", where)} ORDER BY s.updated_at DESC LIMIT @Limit";
        else
        {
            where.Add("s.search @@ q.query");
            sql = $"""
                WITH q AS (SELECT {TsQuery(terms, parameters)} AS query)
                SELECT {Columns} FROM chat_sessions s, q WHERE {string.Join(" AND ", where)}
                ORDER BY ts_rank(s.search, q.query) DESC, s.updated_at DESC LIMIT @Limit
                """;
        }
        return [.. (await QueryAsync<SessionRow>(sql, parameters, ct)).Select(r => r.ToSummary())];
    }

    /// <summary>
    /// Prior work for a new chat (D238): sessions with an outcome that share a ticket or a word with the message. A shared
    /// ticket outranks any amount of shared words.
    /// </summary>
    public async Task<IReadOnlyList<SessionSummary>> RelatedAsync(
        Readable readable, string message, IReadOnlyList<string> tickets, int limit, CancellationToken ct)
    {
        var terms = Terms(message);
        if (terms.Length == 0 && tickets.Count == 0)
            return [];

        var parameters = Parameters(readable, limit);
        parameters.Add("Tickets", tickets.ToArray());
        var (with, from, match, rank) = terms.Length == 0
            ? ("", "chat_sessions s", "s.ticket_keys && @Tickets", "0")
            : ($"WITH q AS (SELECT {TsQuery(terms, parameters)} AS query)", "chat_sessions s, q",
                "(s.ticket_keys && @Tickets OR s.search @@ q.query)", "ts_rank(s.search, q.query)");
        var sql = $"""
            {with}
            SELECT {Columns} FROM {from}
            WHERE {ReadableFilter} AND s.outcome <> '' AND {match}
            ORDER BY (s.ticket_keys && @Tickets) DESC, {rank} DESC, s.updated_at DESC LIMIT @Limit
            """;
        return [.. (await QueryAsync<SessionRow>(sql, parameters, ct)).Select(r => r.ToSummary())];
    }

    /// <summary>
    /// Tickets near <paramref name="ticket"/> (D240): linked on the same readable session, or on readable sessions that match
    /// <paramref name="keywords"/> (the words of the ticket's own sessions).
    /// </summary>
    public async Task<IReadOnlyList<RelatedTicket>> RelatedTicketsAsync(
        Readable readable, string ticket, IReadOnlyList<string> keywords, int limit, CancellationToken ct)
    {
        var parameters = Parameters(readable, limit);
        parameters.Add("Ticket", new[] { ticket });
        parameters.Add("Key", ticket);
        var terms = keywords.Take(MaxTerms).ToArray();
        var (with, from, match, rank) = terms.Length == 0
            ? ("", "chat_sessions s", "s.ticket_keys @> @Ticket", "0")
            : ($"WITH q AS (SELECT {TsQuery(terms, parameters)} AS query)", "chat_sessions s, q",
                "(s.ticket_keys @> @Ticket OR s.search @@ q.query)", "ts_rank(s.search, q.query)");
        var sql = $"""
            {with}
            SELECT t.key AS TicketKey, count(*) AS Sessions,
                   sum({rank} + CASE WHEN s.ticket_keys @> @Ticket THEN 1 ELSE 0 END)::float8 AS Score
            FROM {from}, unnest(s.ticket_keys) AS t(key)
            WHERE {ReadableFilter} AND t.key <> @Key AND {match}
            GROUP BY t.key ORDER BY Score DESC, Sessions DESC, t.key LIMIT @Limit
            """;
        return await QueryAsync<RelatedTicket>(sql, parameters, ct);
    }

    private static DynamicParameters Parameters(Readable readable, int limit) =>
        new(new { Me = readable.UserId, Scopes = readable.Scopes.ToArray(), readable.AllOrg, Limit = limit });

    private static string TsQuery(string[] terms, DynamicParameters parameters)
    {
        for (var i = 0; i < terms.Length; i++)
            parameters.Add($"t{i}", terms[i]);
        return string.Join(" || ", terms.Select((_, i) => $"plainto_tsquery('english', @t{i})"));
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, DynamicParameters parameters, CancellationToken ct) =>
        (await db.Database.GetDbConnection().QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: ct))).AsList();
}
