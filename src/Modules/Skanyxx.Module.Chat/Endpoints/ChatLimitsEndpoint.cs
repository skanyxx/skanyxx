using FastEndpoints;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Chat.Endpoints;

internal sealed class ChatLimitsEndpoint(IOptions<ChatOptions> options) : EndpointWithoutRequest<ChatLimits>
{
    public override void Configure()
    {
        Get("api/chat/limits");
        Options(ChatRoutes.Guard());
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var files = options.Value.Files;
        return Send.OkAsync(new ChatLimits(files.TextExtensions, files.ImageExtensions, files.MaxTextBytes, files.MaxImageBytes, files.MaxFilesPerMessage), ct);
    }
}
