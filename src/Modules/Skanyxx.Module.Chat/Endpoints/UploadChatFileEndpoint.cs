using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>
/// One file (multipart field <c>file</c>), checked by extension and content and scanned for secrets; the answer says
/// whether it holds any. The name keeps only its last segment and printable characters.
/// </summary>
internal sealed class UploadChatFileEndpoint(IMediator mediator) : Endpoint<UploadChatFileRequest>
{
    public override void Configure()
    {
        Post("api/chat/files");
        AllowFileUploads();
        Options(ChatRoutes.Guard(ChatFileOptions.MaxBytes + 64 * 1024));
    }

    public override async Task HandleAsync(UploadChatFileRequest req, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        await req.File.CopyToAsync(bytes, ct);
        var name = new string([.. Path.GetFileName(req.File.FileName.Replace('\\', '/')).Where(c => !char.IsControl(c) && c is not ('"' or '[' or ']'))]).Trim();
        await Send.ResultAsync((await mediator.Send(new UploadChatFileCommand(Caller.UserId(User)!, name, bytes.ToArray()), ct)).ToHttp());
    }
}
