namespace Skanyxx.Module.ChatContext.Domain;

/// <summary>
/// One kagent chat session in the index (D236). Only what was derived from the conversation is kept: never the
/// transcript (kagent keeps that for its TTL, D044). Who may read it is <see cref="Scope"/> (D237).
/// </summary>
public sealed class ChatSession
{
    public const int MaxTitle = 120;
    public const int MaxOutcome = 400;

    /// <summary>kagent's session id.</summary>
    public required string Id { get; set; }

    public required string UserId { get; set; }

    /// <summary><c>namespace/name</c>.</summary>
    public required string AgentRef { get; set; }

    public required string Title { get; set; }

    /// <summary>Memory's scope grammar: <c>personal:{user}</c> (default), <c>team:{id}</c>, <c>department:{id}</c> or <c>company</c>.</summary>
    public required string Scope { get; set; }

    /// <summary>The best <see cref="Extraction.SessionDigest.MaxKeywords"/> of <see cref="KeywordWeights"/>, best first.</summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>Word → weight, summed over turns; capped so a long chat stays a small row.</summary>
    public Dictionary<string, int> KeywordWeights { get; set; } = [];

    public List<string> Categories { get; set; } = [];

    public List<string> TicketKeys { get; set; } = [];

    /// <summary>A short summary of where the conversation got to; "" until the first answer.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>Keywords, categories and ticket keys as one text, for the search vector (see the configuration).</summary>
    public string SearchTerms { get; private set; } = "";

    public int Turns { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Call after changing keywords, categories or tickets.</summary>
    public void RefreshSearchTerms() => SearchTerms = string.Join(' ', [.. Keywords, .. Categories, .. TicketKeys]);
}
