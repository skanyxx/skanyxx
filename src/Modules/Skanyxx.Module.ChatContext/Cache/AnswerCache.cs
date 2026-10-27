using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>
/// D241: the first question of a new conversation, answered again from Dragonfly/Redis when the same person asks the same
/// agent the same thing. Never across people: a person's access scope is the widest one the answer may have used (their
/// personal cards, the teams the agent searched for them), so the key starts with their user id. The key also holds
/// what must miss when it changes: the agent's revision, the tickets, a global epoch (bumped when the model changes, a
/// shared card is written, or the owner clears all) and a per-person generation (bumped when one of their personal cards
/// is written, they clear theirs or forget a session). Entries expire after <see cref="AnswerCacheOptions.TtlMinutes"/>. A store that fails is
/// logged and treated as a miss: the cache saves tokens, it never stops a chat.
/// </summary>
internal sealed partial class AnswerCache(IAnswerStore store, IOptions<ChatContextOptions> options, ILogger<AnswerCache> logger)
{
    private const string Prefix = "skanyxx:chat:";
    private const string Epoch = Prefix + "epoch";
    private const string AllHits = Prefix + "hits";
    private const string AllSaved = Prefix + "saved";

    public bool On => options.Value.AnswerCache.Enabled;

    /// <summary>Null when the store cannot be read (no caching for this turn).</summary>
    public async Task<string?> KeyAsync(string userId, string agentRef, string agentRevision, IReadOnlyList<string> tickets, string message)
    {
        try
        {
            var epoch = await store.CounterAsync(Epoch);
            var generation = await store.CounterAsync(Generation(userId));
            var question = string.Join('\n', agentRef, agentRevision, string.Join(',', tickets.Order(StringComparer.Ordinal)), Normalize(message));
            return $"{Prefix}answer:{userId}:{epoch}:{generation}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(question)))}";
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Answer cache unavailable; asking the agent");
            return null;
        }
    }

    public async Task<CachedAnswer?> GetAsync(string key)
    {
        try
        {
            return await store.GetAsync(key) is { } json ? JsonSerializer.Deserialize<CachedAnswer>(json) : null;
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Answer cache unavailable; asking the agent");
            return null;
        }
    }

    public Task SetAsync(string key, CachedAnswer answer) => Quietly("keep an answer", () =>
        store.SetAsync(key, JsonSerializer.Serialize(answer), TimeSpan.FromMinutes(options.Value.AnswerCache.TtlMinutes)));

    public Task RecordHitAsync(string userId, long tokens) => Quietly("count a hit", async () =>
    {
        await store.IncrementAsync(Hits(userId));
        await store.IncrementAsync(Saved(userId), tokens);
        await store.IncrementAsync(AllHits);
        await store.IncrementAsync(AllSaved, tokens);
    });

    /// <summary>Every answer kept for this person stops matching (they expire on their own).</summary>
    public Task ClearAsync(string userId) => Quietly("clear a person's answers", () => store.IncrementAsync(Generation(userId)));

    /// <summary>Every kept answer stops matching: the model changed, or the owner asked.</summary>
    public Task ClearAllAsync() => Quietly("clear all answers", () => store.IncrementAsync(Epoch));

    public async Task<AnswerCacheStats> StatsAsync(string userId, bool all)
    {
        if (!On)
            return new AnswerCacheStats(false, 0, 0, null, null);
        return new AnswerCacheStats(true,
            await store.CounterAsync(Hits(userId)), await store.CounterAsync(Saved(userId)),
            all ? await store.CounterAsync(AllHits) : null, all ? await store.CounterAsync(AllSaved) : null);
    }

    /// <summary>Case, surrounding and repeated whitespace, and trailing punctuation do not make a question different.</summary>
    internal static string Normalize(string message) =>
        WhitespacePattern().Replace(message.Trim(), " ").TrimEnd('?', '!', '.', ' ').ToLowerInvariant();

    private static string Generation(string userId) => $"{Prefix}generation:{userId}";

    private static string Hits(string userId) => $"{Prefix}hits:{userId}";

    private static string Saved(string userId) => $"{Prefix}saved:{userId}";

    private async Task Quietly(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Answer cache could not {What}", what);
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
