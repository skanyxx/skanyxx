using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// The owner's view of the identity audit (D152), newest first. <paramref name="Before"/>: only entries with a smaller id
/// (the next page). <paramref name="Action"/>: one action exactly. <paramref name="UserId"/>: entries where that person is
/// the actor or the target.
/// </summary>
public sealed record ListAuditQuery(long? Before = null, int Limit = 50, string? Action = null, string? UserId = null)
    : IRequest<Outcome<IReadOnlyList<AuditEntryDto>>>;
