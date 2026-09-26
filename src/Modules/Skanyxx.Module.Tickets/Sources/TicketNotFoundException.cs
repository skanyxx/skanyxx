namespace Skanyxx.Module.Tickets.Sources;

public sealed class TicketNotFoundException(string key) : Exception($"No ticket '{key}'.");
