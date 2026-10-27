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
    public DateTime CreatedAt { get; set; }
    public CardStatus Status { get; set; }
    public string? Body { get; set; }
    public string? Source { get; set; }
    public long? LiftedFromId { get; set; }

    /// <summary>Ticket keys (<c>SDB-58</c>) the card is about: how a later session finds it by ticket.</summary>
    public List<string> Tickets { get; set; } = [];

    /// <summary>Other cards (<c>scope/key</c>), work (<c>chat:</c>/<c>run:</c> ids) or URLs it relates to.</summary>
    public List<string> Links { get; set; } = [];

    /// <summary>Lowercase topic words, for filters and for matching a question that shares no stemmed word.</summary>
    public List<string> Keywords { get; set; } = [];

    public CardOrigin Origin { get; set; }

    /// <summary>The agent that last wrote it, or the agent of the chat it was extracted from.</summary>
    public string? AgentId { get; set; }

    /// <summary>A stale card's replacement, <c>scope/key</c>.</summary>
    public string? SupersededBy { get; set; }

    public NpgsqlTsVector Search { get; set; } = null!;
}
