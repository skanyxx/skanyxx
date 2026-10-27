using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// A card in <paramref name="Scope"/> was written (upsert, lift into, rename). Published by memory after the write; the
/// chat answer cache drops what may depend on it (D241). Carries no card content.
/// </summary>
public sealed record CardsChanged(string Scope) : INotification;
