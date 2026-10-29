using MediatR;

namespace Skanyxx.Core.Platform.Analytics;

/// <summary>
/// Memory cards created per UTC day from <paramref name="From"/> to <paramref name="To"/> (inclusive), in the given
/// <paramref name="Scopes"/> (<c>team:billing</c>, …) or, when null, across every scope. Counts only: no key, scope or
/// text leaves memory. Days without a card are absent.
/// </summary>
public sealed record CardsPerDayQuery(DateOnly From, DateOnly To, IReadOnlyCollection<string>? Scopes = null) : IRequest<IReadOnlyList<DayCount>>;
