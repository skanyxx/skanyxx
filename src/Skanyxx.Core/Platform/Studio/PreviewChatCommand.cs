using MediatR;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>One turn with a proposal's preview, as the signed-in builder or supervisor (D030).</summary>
public sealed record PreviewChatCommand(StudioUser User, int Number, string Message, string? ConversationId)
    : IRequest<Outcome<ChatResponse>>;
