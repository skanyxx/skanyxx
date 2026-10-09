using MediatR;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// The studio reconciler writes the grants of an agent from git (D029: grants in Skanyxx, YAML in git). Replaces them
/// when they differ (<c>true</c> = changed); an empty list removes them. Refused for any name the studio did not claim
/// (an acts-for-users agent, D084, or any principal made by hand, D117) and while the owner has it suspended (D121). Roles were checked when the PR was merged (D024, D091);
/// <paramref name="Actor"/> is for the audit line.
/// </summary>
public sealed record SetStudioGrantsCommand(string Actor, string AgentId, IReadOnlyList<StudioGrant> Grants) : IRequest<Outcome<bool>>;
