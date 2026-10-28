using System.Text.Json;
using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features;

/// <summary><paramref name="UserId"/>'s own conversation's kagent events, raw (D274).</summary>
internal sealed record ConversationEventsQuery(string UserId, string ConversationId) : IRequest<Outcome<IReadOnlyList<JsonElement>>>;
