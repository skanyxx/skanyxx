using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

internal sealed class ChatFileHandler(ChatFiles files) : IRequestHandler<ChatFileQuery, Outcome<StoredChatFile>>
{
    public async Task<Outcome<StoredChatFile>> Handle(ChatFileQuery query, CancellationToken ct) =>
        await files.FindAsync(query.UserId, query.Id, ct) is { } file
            ? Outcome<StoredChatFile>.Ok(file)
            : Outcome<StoredChatFile>.NotFound("No such file.");
}
