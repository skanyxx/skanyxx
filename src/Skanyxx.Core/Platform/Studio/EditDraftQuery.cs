using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>An agent in main as a form, to propose a change to it (D2: an edit is a new PR).</summary>
public sealed record EditDraftQuery(StudioUser User, string Agent) : IRequest<Outcome<AgentDraft>>;
