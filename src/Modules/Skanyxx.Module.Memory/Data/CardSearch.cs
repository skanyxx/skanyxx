using System.Text.RegularExpressions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Contracts;

namespace Skanyxx.Module.Memory.Data;

/// <summary>FTS read path (Dapper): published cards only, any query word matches, best rank first (D013, D037).</summary>
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
        WHERE c.status = 'published' AND c.scope = ANY(@Scopes) AND c.search @@ q.query
        ORDER BY ts_rank(c.search, q.query) DESC, c.updated_at DESC
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

    public async Task<IReadOnlyList<CardHit>> SearchAsync(string query, IReadOnlyList<string> scopes, int limit, CancellationToken ct)
    {
        var terms = Terms(query);
        if (scopes.Count == 0 || terms.Length == 0)
            return [];

        var parameters = new DynamicParameters(new { Scopes = scopes.ToArray(), Limit = limit });
        for (var i = 0; i < terms.Length; i++)
            parameters.Add($"t{i}", terms[i]);
        var tsquery = string.Join(" || ", terms.Select((_, i) => $"plainto_tsquery('english', @t{i})"));

        var rows = await db.Database.GetDbConnection().QueryAsync<CardHit>(
            new CommandDefinition(string.Format(Sql, tsquery), parameters, cancellationToken: ct));
        return rows.AsList();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
