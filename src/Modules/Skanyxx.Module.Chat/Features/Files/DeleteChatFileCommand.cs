using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>Removes one of <paramref name="UserId"/>'s files (an attachment taken back before sending).</summary>
internal sealed record DeleteChatFileCommand(string UserId, string Id) : IRequest<Outcome<bool>>;
