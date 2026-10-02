using NpgsqlTypes;

namespace Skanyxx.Module.Memory.Domain;

public sealed class Card
{
    public long Id { get; set; }
    public required string Scope { get; set; }
    public required string Key { get; set; }
    public int Version { get; set; }
    public CardType Type { get; set; }
    public required string What { get; set; }
    public required string Why { get; set; }
    public required string Who { get; set; }
    public DateTime UpdatedAt { get; set; }
    public CardStatus Status { get; set; }
    public string? Body { get; set; }
    public string? Source { get; set; }
    public long? LiftedFromId { get; set; }
    public NpgsqlTsVector Search { get; set; } = null!;
}
