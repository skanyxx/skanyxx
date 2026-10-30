using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Cache;
using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Domain;
using Skanyxx.Module.ChatContext.Extraction;

namespace Skanyxx.Module.ChatContext.Context;

/// <summary>
/// Chat's <see cref="IChatContext"/> (D260–D265). Before a new conversation: the answer cache (D502), then related sessions
/// (<see cref="SessionSearch.RelatedAsync"/>, readable only). Cards are memory's part (<c>IRelatedMemory</c>, D302).
/// After every turn: the index row, through <see cref="IChatExtractor"/>.
/// </summary>
internal sealed class ChatContextService(
    ChatContextDbContext db, SessionSearch search, SessionAccess access, IChatExtractor extractor, DeterministicExtractor probe, AnswerCache cache,
    IOptions<ChatContextOptions> options, ITokenEstimator estimator, TimeProvider clock, ILogger<ChatContextService> logger) : IChatContext
{
    public const int MaxTitle = 80;

    public async Task<ChatPreparation> PrepareAsync(ChatTurnStart start, CancellationToken ct)
    {
        var me = start.Reader.UserId;
        try
        {
            string? cacheKey = null;
            if (cache.On)
            {
                cacheKey = await cache.KeyAsync(me, start.AgentRef, start.AgentRevision, start.TicketKeys, start.Message);
                if (!start.NoCache && cacheKey is not null && await cache.GetAsync(cacheKey) is { } hit && await StillOneTurnAsync(me, hit, ct))
                {
                    await cache.RecordHitAsync(me, estimator.Count(start.Message + hit.Content));
                    return ChatPreparation.None with { Cached = Answer(hit) };
                }
            }

            var settings = options.Value;
            if (settings.RelatedTokenBudget == 0)
                return ChatPreparation.None with { CacheKey = cacheKey };

            // What the message itself says: tickets written in it count like linked ones, its categories rank.
            var asked = await probe.ExtractAsync(SessionDigest.Empty, new ChatTurn(start.Message, ""), ct);
            var tickets = start.TicketKeys.Union(asked.TicketKeys).ToList();
            var readable = await access.ReadableAsync(start.Reader, ct);
            var sessions = await search.RelatedAsync(readable, start.Message, tickets, asked.Categories, settings.MaxRelatedSessions, ct);
            var block = RelatedBlock.Build(sessions, settings.RelatedTokenBudget, estimator);
            if (block.Text.Length > 0)
                logger.LogInformation("Related work for {UserId}: {Sessions} sessions, ~{Tokens} tokens",
                    me, block.Sessions.Count, block.EstimatedTokens);
            // An answer built on someone else's shared chat is not kept: the share may be withdrawn, or the reader leave
            // the team, and a kept answer would still carry it.
            if (block.Sessions.Any(s => !s.Mine))
                cacheKey = null;
            return new ChatPreparation(block.Text, [.. block.Sessions.Select(Related)], CacheKey: cacheKey);
        }
        catch (DbException ex)
        {
            logger.LogWarning(ex, "Chat index unavailable; the first turn goes without related work");
            return ChatPreparation.None;
        }
    }

    public async Task CompletedAsync(ChatTurnDone done, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            var session = await db.Sessions.FindAsync([done.SessionId], ct);
            if (session is null)
            {
                session = new ChatSession
                {
                    Id = done.SessionId, UserId = done.Reader.UserId, AgentRef = done.AgentRef, Title = Title(done.Message),
                    Scope = SessionScope.Personal(done.Reader.UserId), CreatedAt = now
                };
                db.Sessions.Add(session);
            }

            var current = new SessionDigest(session.KeywordWeights, session.Categories,
                [.. session.TicketKeys.Union(done.TicketKeys).Take(ChatTickets.MaxPerSession)], session.Outcome);
            var digest = await extractor.ExtractAsync(current, new ChatTurn(done.Message, done.Reply), ct);
            session.KeywordWeights = new Dictionary<string, int>(digest.KeywordWeights);
            session.Keywords = [.. digest.Keywords];
            session.Categories = [.. digest.Categories];
            session.TicketKeys = [.. digest.TicketKeys];
            session.Outcome = digest.Outcome;
            session.RefreshSearchTerms();
            session.Turns++;
            session.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        // The person has their answer: no index failure (database, or an extractor that throws) may take it away.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not index chat session {SessionId}", done.SessionId);
        }

        if (done.IsFirst && done.CacheKey is { } key)
            await cache.SetAsync(key, new CachedAnswer(done.SessionId, done.Reply, now));
    }

    /// <summary>
    /// D502: a hit reopens the conversation it came from, so it is served only while that conversation is still just this
    /// question and its answer. One that went on would open on its later turns, and a new chat would show another
    /// question's answer; the agent is asked again instead, and the new conversation replaces the kept answer.
    /// </summary>
    private async Task<bool> StillOneTurnAsync(string userId, CachedAnswer hit, CancellationToken ct) =>
        await db.Sessions.AsNoTracking().AnyAsync(s => s.Id == hit.ConversationId && s.UserId == userId && s.Turns == 1, ct);

    /// <summary>The first line of what the person asked, folded and cut.</summary>
    internal static string Title(string message)
    {
        var line = string.Join(' ', message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []);
        return TextCut.Cut(line, MaxTitle);
    }

    private static RelatedSession Related(SessionSummary s) =>
        new(s.SessionId, s.Title, s.AgentRef, s.Outcome, s.TicketKeys, s.UpdatedAt, s.Mine);

    private static ChatResponse Answer(CachedAnswer hit) => new()
    {
        ConversationId = hit.ConversationId,
        Message = new ChatMessage { Role = "assistant", Content = hit.Content, SessionId = hit.ConversationId, Timestamp = hit.CreatedAt },
        Cached = true
    };
}
