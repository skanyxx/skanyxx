using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

/// <summary><paramref name="UserId"/>'s own file; anyone else's is "not found".</summary>
internal sealed record ChatFileQuery(string UserId, string Id) : IRequest<Outcome<StoredChatFile>>;
