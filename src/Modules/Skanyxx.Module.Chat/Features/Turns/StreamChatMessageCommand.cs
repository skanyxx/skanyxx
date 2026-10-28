using MediatR;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary><see cref="Turn"/>, streamed: refusals come back as the outcome, before any event is written.</summary>
internal sealed record StreamChatMessageCommand(SendChatMessageCommand Turn) : IRequest<Outcome<ChatStream>>;
