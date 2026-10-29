using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>
/// <paramref name="UserId"/> uploads a file to send with a later turn (the file name without any folder).
/// <paramref name="Size"/> is the uploaded part's measured length; <paramref name="Bytes"/> are read only when it is within the
/// kind's limit, so an oversized upload is refused without ever being read into memory.
/// </summary>
internal sealed record UploadChatFileCommand(string UserId, string Name, long Size, byte[] Bytes) : IRequest<Outcome<ChatFileInfo>>;
