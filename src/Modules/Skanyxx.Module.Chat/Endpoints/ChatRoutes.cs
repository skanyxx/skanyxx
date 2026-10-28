using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>Every Chat endpoint: CORS off (no other origin drives it with the person's cookie) and a body cap.</summary>
internal static class ChatRoutes
{
    public const int SmallBody = 16 * 1024;

    public static Action<RouteHandlerBuilder> Guard(int maxBodyBytes = SmallBody) =>
        b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(maxBodyBytes));
}
