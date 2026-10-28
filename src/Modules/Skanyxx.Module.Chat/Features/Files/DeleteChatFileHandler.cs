using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

internal sealed class DeleteChatFileHandler(ChatFiles files) : IRequestHandler<DeleteChatFileCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(DeleteChatFileCommand command, CancellationToken ct)
    {
        if (await files.FindAsync(command.UserId, command.Id, ct) is not { } file)
            return Outcome<bool>.NotFound("No such file.");
        files.Delete(command.UserId, file);
        return Outcome<bool>.Ok(true);
    }
}
