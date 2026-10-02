namespace Skanyxx.Module.Tickets.Sources;

/// <summary>The source could not answer. The message is safe to show: no credential, no JQL, no upstream body.</summary>
public sealed class TicketSourceException(string message) : Exception(message);
