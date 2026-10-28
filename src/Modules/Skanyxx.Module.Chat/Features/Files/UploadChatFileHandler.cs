using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

internal sealed class UploadChatFileHandler(ChatFiles files) : IRequestHandler<UploadChatFileCommand, Outcome<ChatFileInfo>>
{
    public async Task<Outcome<ChatFileInfo>> Handle(UploadChatFileCommand command, CancellationToken ct) =>
        await files.SaveAsync(command.UserId, command.Name, command.Bytes, ct) is { } file
            ? Outcome<ChatFileInfo>.Created(ChatFileInfo.From(file))
            : Outcome<ChatFileInfo>.RateLimited(
                $"You keep {files.Options.MaxFilesPerPerson} files in Chat, the most allowed. Delete a conversation you no longer need, then attach this again.");
}
