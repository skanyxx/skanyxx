using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// Factory (D020, D022): a builder describes the agent in words; the factory agent drafts the studio form, which the
/// builder reviews and proposes like any other. It never opens a PR or touches kagent by itself.
/// </summary>
public sealed record FactoryDraftCommand(StudioUser User, string Request) : IRequest<Outcome<AgentDraft>>;
