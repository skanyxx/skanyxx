using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Domain;

namespace Skanyxx.Module.ChatContext.Features;

internal static class SessionSummaries
{
    public static SessionSummary From(ChatSession s, string reader) => new(
        s.Id, s.Title, s.AgentRef, s.Scope, [.. s.Keywords], [.. s.Categories], [.. s.TicketKeys], s.Outcome, s.Turns,
        s.CreatedAt, s.UpdatedAt, s.UserId == reader);
}
