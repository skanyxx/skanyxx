using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Coordination;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Sessions;

/// <summary>
/// Finished work → one card in its owner's personal scope, written as that person through the one upsert path (so the
/// owner rule, versions, dedupe, rate limits and history all hold). A chat session is <c>chat-&lt;session&gt;</c>: born
/// on its first turn with the question as its <c>what</c>, its <c>why</c> following the latest answer, its tickets
/// accumulating. A run is <c>run-&lt;ticket&gt;-&lt;run&gt;</c>. A full personal scope makes room by dropping its oldest
/// extracted card, never one a person wrote. Never throws for a memory failure: the work it records is already done.
/// </summary>
internal sealed partial class CardExtractor(
    IMediator mediator, MemoryDbContext db, SearchCache cache, IOptions<MemoryOptions> options, ILogger<CardExtractor> logger)
    : ICardExtractor
{
    private const int KeywordsPerTurn = 6;

    private readonly MemoryOptions _options = options.Value;

    public Task<MemoryCardRef?> ChatTurnAsync(ChatTurnDigest turn, CancellationToken ct)
    {
        if (!_options.Extraction.Chats || !Identifier.IsValid(turn.UserId) || !CardLinks.IsValid($"chat:{turn.SessionId}"))
            return Task.FromResult<MemoryCardRef?>(null);
        return GuardAsync(turn.UserId, async () =>
        {
            var scope = Scope.Personal(turn.UserId).ToString();
            var key = SessionText.Slug($"chat-{turn.SessionId}");
            var existing = await FindAsync(scope, key, ct);
            var mentioned = CardTickets.FindIn($"{turn.Message}\n{turn.Reply}", CardLimits.Tickets);
            if (existing is null && SessionText.Words(turn.Message) < _options.Extraction.MinWords && mentioned.Count == 0)
                return null;

            return await WriteAsync(existing, new UpsertCardCommand(
                new MemoryCaller(turn.UserId, null), scope, key, existing?.Version ?? 0, "fact",
                existing?.What ?? SessionText.Line($"Chat with {turn.AgentId}: {turn.Message}", CardLimits.What),
                Why(turn.Reply, "No answer yet."),
                Source: $"chat:{turn.SessionId}",
                Tickets: Merge(existing?.Tickets, mentioned, CardLimits.Tickets),
                Keywords: Merge(existing?.Keywords, SessionText.Keywords(turn.Message).Take(KeywordsPerTurn), CardLimits.Keywords),
                Origin: CardOrigin.Chat,
                ViaAgent: Identifier.IsValid(turn.AgentId) ? turn.AgentId : null), ct);
        });
    }

    public Task<MemoryCardRef?> TicketRunAsync(TicketRunFinished run, CancellationToken ct)
    {
        if (!_options.Extraction.Runs || !Identifier.IsValid(run.OwnerUserId) || !CardTickets.IsValid(run.TicketKey))
            return Task.FromResult<MemoryCardRef?>(null);
        return GuardAsync(run.OwnerUserId, async () =>
        {
            var scope = Scope.Personal(run.OwnerUserId).ToString();
            var key = SessionText.Slug($"run-{run.TicketKey}-{run.RunId:N}");
            var existing = await FindAsync(scope, key, ct);
            var labels = run.Labels.Select(SessionText.Slug).Where(CardLinks.IsKeyword);
            return await WriteAsync(existing, new UpsertCardCommand(
                new MemoryCaller(run.OwnerUserId, null), scope, key, existing?.Version ?? 0, "fact",
                SessionText.Line($"{run.TicketKey} {run.TicketTitle}: {run.PipelineName} run {(run.Succeeded ? "succeeded" : "failed")}", CardLimits.What),
                Why(run.Outcome, "No stage output."),
                Source: $"run:{run.RunId}",
                Tickets: Merge([run.TicketKey], run.RelatedTickets.Where(CardTickets.IsValid), CardLimits.Tickets),
                Links: run.TicketUrl is { } url && CardLinks.IsValid(url) ? [url] : [],
                Keywords: Merge(null, labels.Concat(SessionText.Keywords(run.TicketTitle)), CardLimits.Keywords),
                Origin: CardOrigin.Run), ct);
        });
    }

    private async Task<MemoryCardRef?> WriteAsync(Card? existing, UpsertCardCommand command, CancellationToken ct)
    {
        if (existing is null && !await MakeRoomAsync(command.Scope, ct))
            return null;

        var outcome = await mediator.Send(command, ct);
        // Two turns of one session finishing together: the second re-reads and writes over the first.
        if (outcome.Status == OutcomeStatus.Conflict && await FindAsync(command.Scope, command.Key, ct) is { } current)
            outcome = await mediator.Send(command with { Version = current.Version }, ct);

        if (outcome is { Status: OutcomeStatus.Ok or OutcomeStatus.Created, Value: { } card })
            return new MemoryCardRef(card.Scope, card.Key, card.What);
        LogNotKept(logger, command.Scope, command.Key, outcome.Status, outcome.Message);
        return null;
    }

    /// <summary>At the personal quota, the oldest extracted card goes; a scope of only hand-written cards keeps them all.</summary>
    private async Task<bool> MakeRoomAsync(string scope, CancellationToken ct)
    {
        if (await db.Cards.CountAsync(c => c.Scope == scope, ct) < _options.MaxCardsPerPersonalScope)
            return true;
        var dropped = await db.Cards
            .Where(c => c.Scope == scope && c.Origin != CardOrigin.Manual)
            .OrderBy(c => c.UpdatedAt)
            .Take(1)
            .ExecuteDeleteAsync(ct);
        if (dropped > 0)
            await cache.InvalidateAsync(scope);
        return dropped > 0;
    }

    private async Task<MemoryCardRef?> GuardAsync(string userId, Func<Task<MemoryCardRef?>> extract)
    {
        try
        {
            return await extract();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Includes a ValidationException: text that slipped past the cuts is not worth failing the chat or run for.
            LogFailed(logger, userId, ex is ValidationException ? "invalid" : ex.GetType().Name, ex);
            return null;
        }
    }

    private Task<Card?> FindAsync(string scope, string key, CancellationToken ct) =>
        db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Scope == scope && c.Key == key, ct);

    private static string Why(string text, string otherwise) =>
        SessionText.Gist(text, CardLimits.Why) is { Length: > 0 } gist ? gist : otherwise;

    private static List<string> Merge(IEnumerable<string>? kept, IEnumerable<string> added, int max) =>
        [.. (kept ?? []).Concat(added).Distinct().Take(max)];

    [LoggerMessage(Level = LogLevel.Information, Message = "No card kept for {Scope}/{Key}: {Status} {Reason}")]
    private static partial void LogNotKept(ILogger logger, string scope, string key, OutcomeStatus status, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Extracting a card for {UserId} failed ({Kind}); the work is not in memory")]
    private static partial void LogFailed(ILogger logger, string userId, string kind, Exception ex);
}
