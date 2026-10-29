using FastEndpoints;
using MediatR;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>
/// One file (multipart field <c>file</c>), checked by extension and content and scanned for secrets; the answer says
/// whether it holds any. The name keeps only its last segment and printable characters. The body cap is the largest
/// kind's limit; a file over its own kind's limit is refused before it is read into memory, and one that fits is read
/// once, straight into the array the command carries.
/// </summary>
internal sealed class UploadChatFileEndpoint(IMediator mediator, ChatFiles files) : Endpoint<UploadChatFileRequest>
{
    private const int MultipartOverhead = 64 * 1024;

    public override void Configure()
    {
        Post("api/chat/files");
        AllowFileUploads();
        var limits = Config.GetSection(ChatOptions.Section).Get<ChatOptions>()?.Files ?? new ChatFileOptions();
        Options(ChatRoutes.Guard(Math.Max(limits.MaxTextBytes, limits.MaxImageBytes) + MultipartOverhead));
    }

    public override async Task HandleAsync(UploadChatFileRequest req, CancellationToken ct)
    {
        var name = new string([.. Path.GetFileName(req.File.FileName.Replace('\\', '/')).Where(c => !char.IsControl(c) && c is not ('"' or '[' or ']'))]).Trim();
        var size = req.File.Length;
        var bytes = size > 0 && size <= files.MaxBytesOf(name) ? new byte[size] : [];
        if (bytes.Length > 0)
        {
            await using var content = req.File.OpenReadStream();
            await content.ReadExactlyAsync(bytes, ct);
        }
        await Send.ResultAsync((await mediator.Send(new UploadChatFileCommand(Caller.UserId(User)!, name, size, bytes), ct)).ToHttp());
    }
}
