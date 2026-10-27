using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Cache;
using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Domain;
using Skanyxx.Module.ChatContext.Extraction;

namespace Skanyxx.Module.ChatContext.Context;

/// <summary>
/// Chat's <see cref="IChatContext"/> (D236–D241). Before a new conversation: the answer cache, then related sessions
/// (<see cref="SessionSearch.RelatedAsync"/>, readable only) and published <c>company</c> cards. Company is what every
/// agent searches by default (D040), so injection never hands an agent a card its grants would not show it; the person's
/// own cards reach the agent through its own memory search, as that person (D084). After every turn: the index row,
/// through <see cref="IChatExtractor"/>.
/// </summary>
internal sealed class ChatContextService(
    ChatContextDbContext db, SessionSearch search, SessionAccess access, IChatExtractor extractor, DeterministicExtractor probe, AnswerCache cache,
    IMediator mediator, IOptions<ChatContextOptions> options, TimeProvider clock, ILogger<ChatContextService> logger) : IChatContext
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
                if (!start.NoCache && cacheKey is not null && await cache.GetAsync(cacheKey) is { } hit)
                {
                    await cache.RecordHitAsync(me, (start.Message.Length + hit.Content.Length) / RelatedBlock.CharsPerToken);
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
            var cards = await RelatedCardsAsync(start.Message, settings.MaxRelatedCards, ct);
            var block = RelatedBlock.Build(sessions, cards, settings.RelatedTokenBudget);
            if (block.Text.Length > 0)
                logger.LogInformation("Related work for {UserId}: {Sessions} sessions, {Cards} cards, ~{Tokens} tokens",
                    me, block.Sessions.Count, block.Cards.Count, block.EstimatedTokens);
            // An answer built on someone else's shared chat is not kept: the share may be withdrawn, or the reader leave
            // the team, and a kept answer would still carry it.
            if (block.Sessions.Any(s => !s.Mine))
                cacheKey = null;
            return new ChatPreparation(block.Text, [.. block.Sessions.Select(Related)], block.Cards, CacheKey: cacheKey);
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

    private async Task<IReadOnlyList<RelatedCard>> RelatedCardsAsync(string message, int limit, CancellationToken ct)
    {
        var text = CardQuery(message);
        if (limit == 0 || text.Length == 0)
            return [];
        return [.. (await mediator.Send(new CompanyCardsQuery(text, limit), ct)).Select(c => new RelatedCard(c.Scope, c.Key, c.What, c.Why))];
    }

    /// <summary>The message's search words, joined within the library's query limit.</summary>
    private static string CardQuery(string message)
    {
        var text = "";
        foreach (var term in SessionSearch.Terms(message))
        {
            if (text.Length + term.Length + 1 > CardFormat.MaxQueryLength)
                break;
            text = text.Length == 0 ? term : $"{text} {term}";
        }
        return text;
    }
}
