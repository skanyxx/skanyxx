using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// The owner's emergency stop for a merged studio agent (D121): suspended, it loses its memory secret and grants at once
/// and leaves kagent, whatever main says, until the owner resumes it. Owner only.
/// </summary>
public sealed record SuspendAgentCommand(StudioUser User, string Agent, bool Suspended) : IRequest<Outcome<StudioResult>>;
