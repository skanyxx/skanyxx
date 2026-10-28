using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>An agent in main as a form, to propose a change to it (D2: an edit is a new PR).</summary>
/// <param name="FromAgentsPage">Read by the Agents page to keep what its form does not show on a proposal (D312).</param>
public sealed record EditDraftQuery(StudioUser User, string Agent, bool FromAgentsPage = false) : IRequest<Outcome<AgentDraft>>;
