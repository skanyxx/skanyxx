using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.ChatContext.Data;
using Skanyxx.Module.ChatContext.Domain;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>A conversation renamed in Chat has the same title in the index (history search, ticket views).</summary>
internal sealed class RenamedConversationKeepsTitle(ChatContextDbContext db) : INotificationHandler<ChatConversationRenamed>
{
    public Task Handle(ChatConversationRenamed renamed, CancellationToken ct)
    {
        var title = renamed.Title.Length <= ChatSession.MaxTitle ? renamed.Title : renamed.Title[..ChatSession.MaxTitle];
        return db.Sessions.Where(s => s.Id == renamed.ConversationId && s.UserId == renamed.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, title), ct);
    }
}
