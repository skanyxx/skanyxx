using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Endpoints;

/// <param name="Mine">Only the caller's own sessions: the Chat page's history.</param>
internal sealed class SearchSessionsRequest
{
    public string? Q { get; set; }
    public string? Ticket { get; set; }
    public string? Category { get; set; }
    public string? Agent { get; set; }
    public bool Mine { get; set; }

    /// <summary>A blank parameter filters nothing.</summary>
    public SessionFilter ToFilter() => new(Value(Q), Value(Ticket), Value(Category), Value(Agent), Mine);

    private static string? Value(string? parameter) => string.IsNullOrWhiteSpace(parameter) ? null : parameter.Trim();
}
