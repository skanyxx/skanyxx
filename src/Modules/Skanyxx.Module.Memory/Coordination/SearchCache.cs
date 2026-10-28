using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Skanyxx.Module.Memory.Contracts;

namespace Skanyxx.Module.Memory.Coordination;

/// <summary>
/// Search results in Dragonfly, keyed by the exact scope set searched: two callers share an entry only when they may
/// search exactly the same scopes, so a cached result never crosses access (a membership or grant change is a different
/// key). Each scope has a generation that every write to it bumps, and the generations are part of the key, so a write
/// ends every cached result that could hold its scope at once; the TTL only bounds the size.
/// </summary>
public sealed class SearchCache(Dragonfly dragonfly, IOptions<MemoryOptions> options)
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.Dragonfly.SearchCacheSeconds);

    public async Task<IReadOnlyList<CardHit>> GetOrSearchAsync(
        IReadOnlyList<string> scopes, string query, IReadOnlyList<string> tickets, int limit, Func<Task<IReadOnlyList<CardHit>>> search)
    {
        if (await dragonfly.DatabaseAsync() is not { } redis)
            return await search();

        RedisKey key;
        try
        {
            var ordered = scopes.Order(StringComparer.Ordinal).ToArray();
            var generations = await redis.StringGetAsync([.. ordered.Select(Generation)]);
            key = Entry(ordered, generations, query, tickets, limit);
            if (await redis.StringGetAsync(key) is { HasValue: true } cached)
                return JsonSerializer.Deserialize<CardHit[]>((string)cached!)!;
        }
        catch (Exception ex) when (Dragonfly.IsFailure(ex))
        {
            dragonfly.Failed(ex);
            return await search();
        }

        var hits = await search();
        try
        {
            await redis.StringSetAsync(key, JsonSerializer.Serialize(hits), _ttl);
        }
        catch (Exception ex) when (Dragonfly.IsFailure(ex))
        {
            dragonfly.Failed(ex);
        }
        return hits;
    }

    /// <summary>After a committed write to <paramref name="scope"/>: no cached search over it is served again.</summary>
    public async Task InvalidateAsync(string scope)
    {
        if (await dragonfly.DatabaseAsync() is not { } redis)
            return;
        try
        {
            await redis.StringIncrementAsync(Generation(scope));
        }
        catch (Exception ex) when (Dragonfly.IsFailure(ex))
        {
            // The entry outlives the write by at most its TTL; Postgres has the card either way.
            dragonfly.Failed(ex);
        }
    }

    private RedisKey Generation(string scope) => dragonfly.Key($"gen:{scope}");

    private RedisKey Entry(string[] scopes, RedisValue[] generations, string query, IReadOnlyList<string> tickets, int limit)
    {
        var text = new StringBuilder();
        for (var i = 0; i < scopes.Length; i++)
            text.Append(scopes[i]).Append('=').Append(generations[i].IsNull ? "0" : (string)generations[i]!).Append('\n');
        text.Append(limit).Append('\n').Append(string.Join(',', tickets)).Append('\n').Append(query);
        return dragonfly.Key($"search:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))}");
    }
}
