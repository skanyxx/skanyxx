using Microsoft.AspNetCore.Http;

namespace Skanyxx.Module.Chat.Endpoints;

internal sealed class UploadChatFileRequest
{
    public IFormFile File { get; set; } = null!;
}
