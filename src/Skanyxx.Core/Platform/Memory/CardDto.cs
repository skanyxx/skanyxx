namespace Skanyxx.Core.Platform.Memory;

/// <summary>What a human sees in the library: the card plus body and source pointer.</summary>
/// <param name="BodyStored">The body is in the object store: <c>Body</c> is only its excerpt and <c>Source</c> its
/// pointer; the full text is at <c>GET api/memory/cards/{scope}/{key}/body</c> (D183). Writing back an edited excerpt
/// with that pointer is refused; send the edited full text, or <c>source: ""</c> to replace it with a short body.</param>
/// <param name="Origin"><c>manual</c>, or <c>chat</c>/<c>run</c> for a card memory extracted from that work.</param>
public sealed record CardDto(
    string Scope, string Key, int Version, string Type, string What, string Why, string Who, DateTime UpdatedAt,
    string Status, string? Body, string? Source, long? LiftedFromId, bool BodyStored = false,
    IReadOnlyList<string>? Tickets = null, IReadOnlyList<string>? Links = null, IReadOnlyList<string>? Keywords = null,
    string Origin = "manual", string? AgentId = null, string? SupersededBy = null, DateTime CreatedAt = default);
