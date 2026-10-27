using Skanyxx.Core.Platform;
using MediatR;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// Version 0 creates; version n replaces the card only if it is still at n (D041).
/// A null body/source leaves the stored one untouched: agents never see those fields (D014), so they cannot resend them.
/// Null tickets/links/keywords likewise leave the stored ones. <paramref name="Origin"/> counts on create only;
/// <paramref name="ViaAgent"/> names the agent of the chat a person's card was extracted from.
/// <paramref name="EvictExtracted"/>: a create into a full scope may drop the scope's oldest extracted card (extraction only).
/// </summary>
public sealed record UpsertCardCommand(
    MemoryCaller Caller, string Scope, string Key, int Version, string Type, string What, string Why,
    string? Body = null, string? Source = null,
    IReadOnlyList<string>? Tickets = null, IReadOnlyList<string>? Links = null, IReadOnlyList<string>? Keywords = null,
    CardOrigin Origin = CardOrigin.Manual, string? ViaAgent = null, bool EvictExtracted = false) : IRequest<Outcome<Card>>;
