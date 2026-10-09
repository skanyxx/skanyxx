using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>Records (or, on the owner's confirmation, replaces) the agent repo the reconciler trusts (D119).</summary>
public sealed record RecordStudioRepoCommand(string Actor, StudioRepoIdentity Repo) : IRequest<Outcome<bool>>;
