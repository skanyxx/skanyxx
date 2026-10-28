namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// A version a write replaced: the whole card as it was — body (or its excerpt) and source pointer included, which stay
/// valid because stored objects are never deleted (D182) — so "what did we decide before" has a complete answer (C-5).
/// </summary>
public sealed class CardVersion
{
    public long Id { get; set; }
    public long CardId { get; set; }
    public int Version { get; set; }
    public required string Scope { get; set; }
    public required string Key { get; set; }
    public CardType Type { get; set; }
    public required string What { get; set; }
    public required string Why { get; set; }
    public required string Who { get; set; }
    public CardStatus Status { get; set; }
    public List<string> Tickets { get; set; } = [];
    public List<string> Links { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public string? Body { get; set; }
    public string? Source { get; set; }
    public CardOrigin Origin { get; set; }
    public string? AgentId { get; set; }
    public string? SupersededBy { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static CardVersion Of(Card card) => new()
    {
        CardId = card.Id, Version = card.Version, Scope = card.Scope, Key = card.Key, Type = card.Type, What = card.What,
        Why = card.Why, Who = card.Who, Status = card.Status, Tickets = [.. card.Tickets], Links = [.. card.Links], Keywords = [.. card.Keywords],
        Body = card.Body, Source = card.Source, Origin = card.Origin, AgentId = card.AgentId, SupersededBy = card.SupersededBy,
        UpdatedAt = card.UpdatedAt
    };
}
