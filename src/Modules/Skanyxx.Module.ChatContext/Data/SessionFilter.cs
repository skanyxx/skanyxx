namespace Skanyxx.Module.ChatContext.Data;

/// <summary>What to search for; every set field narrows. <paramref name="Mine"/>: only the reader's own sessions (history).</summary>
public sealed record SessionFilter(string? Text = null, string? Ticket = null, string? Category = null, string? Agent = null, bool Mine = false);
