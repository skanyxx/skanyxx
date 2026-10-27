namespace Skanyxx.Module.ChatContext.Data;

/// <summary>A ticket whose readable sessions share a ticket or words with the asked one; higher <paramref name="Score"/> is closer.</summary>
public sealed record RelatedTicket(string TicketKey, long Sessions, double Score);
