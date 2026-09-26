using Skanyxx.Core.Platform;
using MediatR;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// Version 0 creates; version n replaces the card only if it is still at n (D041).
/// A null body/source leaves the stored one untouched: agents never see those fields (D014), so they cannot resend them.
/// </summary>
public sealed record UpsertCardCommand(
    Caller Caller, string Scope, string Key, int Version, string Type, string What, string Why,
    string? Body = null, string? Source = null) : IRequest<Outcome<Card>>;
