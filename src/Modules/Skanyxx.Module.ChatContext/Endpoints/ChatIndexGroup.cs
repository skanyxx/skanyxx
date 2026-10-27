using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.ChatContext.Endpoints;

/// <summary>
/// Every chat-index endpoint: a signed-in user (FastEndpoints' default, against the Host's schemes), CORS off, a small
/// body cap. Under <c>api/chat</c>, beside Chat's own endpoints.
/// </summary>
internal sealed class ChatIndexGroup : Group
{
    public const int MaxBodyBytes = 16 * 1024;

    public ChatIndexGroup() =>
        Configure("api/chat", ep =>
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))));
}
