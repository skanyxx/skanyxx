using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.ChatContext.Cache;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>A conversation its owner deleted in Chat leaves the index, and the owner's cached answers with it (as a forget does).</summary>
internal sealed class DeletedConversationLeavesIndex(ChatContextDbContext db, AnswerCache cache) : INotificationHandler<ChatConversationDeleted>
{
    public async Task Handle(ChatConversationDeleted deleted, CancellationToken ct)
    {
        await db.Sessions.Where(s => s.Id == deleted.ConversationId && s.UserId == deleted.UserId).ExecuteDeleteAsync(ct);
        if (cache.On)
            await cache.ClearAsync(deleted.UserId);
    }
}
