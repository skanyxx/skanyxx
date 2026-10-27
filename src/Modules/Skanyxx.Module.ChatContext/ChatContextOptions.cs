using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Skanyxx.Module.ChatContext;

public sealed class ChatContextOptions
{
    public const string Section = "ChatContext";

    /// <summary>Bound from <c>ConnectionStrings:Memory</c>: the index lives next to the cards (memory-stack.md).</summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// D238: the most the related-work block may add to a first message, in estimated tokens (4 characters each).
    /// 0 turns injection off; the index is still kept and searchable.
    /// </summary>
    [Range(0, 4_000)]
    public int RelatedTokenBudget { get; set; } = 600;

    [Range(0, 10)]
    public int MaxRelatedSessions { get; set; } = 3;

    [Range(0, 10)]
    public int MaxRelatedCards { get; set; } = 3;

    /// <summary>
    /// D239: category → word stems; a session is in a category when one of its words starts with a stem. Replaces the
    /// built-in set (<see cref="Extraction.DefaultCategories"/>) when not empty.
    /// </summary>
    public Dictionary<string, string[]> Categories { get; set; } = [];

    /// <summary>D239: ticket projects recognised in chat text (e.g. <c>SDB</c>). Empty: any key shape, minus well-known non-tickets (UTF-8, SHA-256…).</summary>
    public string[] TicketProjects { get; set; } = [];

    [Range(1, 1_000)]
    public int MaxPoolSize { get; set; } = 20;

    public AnswerCacheOptions AnswerCache { get; set; } = new();

    /// <summary>Same pool split as memory's and tickets': a module-suffixed Application Name.</summary>
    internal NpgsqlConnectionStringBuilder ConnectionSettings()
    {
        var settings = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = MaxPoolSize };
        settings.ApplicationName = $"{(string.IsNullOrEmpty(settings.ApplicationName) ? "skanyxx" : settings.ApplicationName)}-chatcontext";
        return settings;
    }
}
