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
/// extracted card, never one a person wrote — in the same transaction as the new card (<see cref="ScopeCapacity"/>). Never throws for a memory failure: the work it records is already done.
/// </summary>
internal sealed partial class CardExtractor(
    IMediator mediator, MemoryDbContext db, IOptions<MemoryOptions> options, ILogger<CardExtractor> logger)
    : ICardExtractor
{
    private const int KeywordsPerTurn = 6;
    private const int MaxAttempts = 5;

    private readonly MemoryOptions _options = options.Value;

    public Task<MemoryCardRef?> ChatTurnAsync(ChatTurnDigest turn, CancellationToken ct)
    {
        if (!_options.Extraction.Chats || !Identifier.IsValid(turn.UserId) || !CardLinks.IsValid($"chat:{turn.SessionId}"))
            return Task.FromResult<MemoryCardRef?>(null);
        return GuardAsync(turn.UserId, async () =>
        {
            var scope = Scope.Personal(turn.UserId).ToString();
            var key = SessionText.Slug($"chat-{turn.SessionId}");
            // Redacted before anything is cut or indexed: a pasted key must not become a card, a keyword or a prompt line.
            var (message, reply) = (SecretText.Redact(turn.Message), SecretText.Redact(turn.Reply));
            var mentioned = CardTickets.FindIn($"{message}\n{reply}", CardLimits.Tickets);
            var words = SessionText.Keywords(message).Take(KeywordsPerTurn).ToList();

            return await WriteAsync(scope, key, existing =>
                existing is null && SessionText.Words(message) < _options.Extraction.MinWords && mentioned.Count == 0
                    ? null
                    : new UpsertCardCommand(
                        new MemoryCaller(turn.UserId, null), scope, key, existing?.Version ?? 0, "fact",
                        existing?.What ?? SessionText.Line($"Chat with {turn.AgentId}: {message}", CardLimits.What),
                        Why(reply, "No answer yet."),
                        Source: $"chat:{turn.SessionId}",
                        Tickets: Merge(existing?.Tickets, mentioned, CardLimits.Tickets),
                        Keywords: Merge(existing?.Keywords, words, CardLimits.Keywords),
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
            var labels = run.Labels.Select(SessionText.Slug).Where(CardLinks.IsKeyword);
            var (title, outcome) = (SecretText.Redact(run.TicketTitle), SecretText.Redact(run.Outcome));
            return await WriteAsync(scope, key, existing => new UpsertCardCommand(
                new MemoryCaller(run.OwnerUserId, null), scope, key, existing?.Version ?? 0, "fact",
                SessionText.Line($"{run.TicketKey} {title}: {run.PipelineName} run {(run.Succeeded ? "succeeded" : "failed")}", CardLimits.What),
                Why(outcome, "No stage output."),
                Source: $"run:{run.RunId}",
                Tickets: Merge([run.TicketKey], run.RelatedTickets.Where(CardTickets.IsValid), CardLimits.Tickets),
                Links: run.TicketUrl is { } url && CardLinks.IsValid(url) && !SecretText.HasCredential(url) ? [url] : [],
                Keywords: Merge(null, labels.Concat(SessionText.Keywords(title)), CardLimits.Keywords),
                Origin: CardOrigin.Run), ct);
        });
    }

    /// <summary>
    /// Builds the write from the card as it is now and sends it; on a conflict (another turn of the same session, or a
    /// first turn racing a second) builds it again from the newer card, so every turn's tickets and keywords are merged
    /// into what the other writer left rather than replayed over it (C-3). Bounded: a session that keeps losing is logged.
    /// </summary>
    private async Task<MemoryCardRef?> WriteAsync(string scope, string key, Func<Card?, UpsertCardCommand?> build, CancellationToken ct)
    {
        Outcome<Card>? outcome = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (build(await FindAsync(scope, key, ct)) is not { } command)
                return null;
            outcome = await mediator.Send(command with { EvictExtracted = true }, ct);
            if (outcome.Status != OutcomeStatus.Conflict)
                break;
        }

        if (outcome is { Status: OutcomeStatus.Ok or OutcomeStatus.Created, Value: { } card })
            return new MemoryCardRef(card.Scope, card.Key, card.What);
        LogNotKept(logger, scope, key, outcome!.Status, outcome.Message);
        return null;
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
