namespace Skanyxx.Module.Memory.Domain;

/// <summary>A version a write replaced: the card as it was, so "what did we decide before" has an answer.</summary>
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
    public DateTime UpdatedAt { get; set; }

    public static CardVersion Of(Card card) => new()
    {
        CardId = card.Id, Version = card.Version, Scope = card.Scope, Key = card.Key, Type = card.Type, What = card.What,
        Why = card.Why, Who = card.Who, Status = card.Status, Tickets = [.. card.Tickets], UpdatedAt = card.UpdatedAt
    };
}
