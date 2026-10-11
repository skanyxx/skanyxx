using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Sandboxes.Features;

/// <summary>
/// The sandboxes side of D076/D077: memory signs and revokes the tokens (through MediatR; this module never sees the
/// key), this module puts them in the sandbox's env. Off while <c>Sandboxes:MemoryMcpUrl</c> is empty: then nothing is
/// issued and nothing is revoked (the memory module need not be loaded at all).
/// </summary>
internal sealed class MemoryAccess(IMediator mediator, IOptions<SandboxesOptions> options)
{
    public bool On => options.Value.MemoryMcpUrl.Length > 0;

    public string Url => options.Value.MemoryMcpUrl;

    public Task<Outcome<SandboxMemoryToken>> IssueAsync(string sandboxName, string agentId, string owner, CancellationToken ct) =>
        mediator.Send(new IssueSandboxMemoryTokenCommand(sandboxName, agentId, owner), ct);

    /// <summary>
    /// Null once revoked, or when there was nothing to revoke; otherwise memory's refusal, which the caller returns: a
    /// stop must not report success while the token still works. An id that is not <c>ax-&lt;name&gt;-&lt;nonce&gt;</c>
    /// (a sandbox made outside Skanyxx can carry anything in <c>SKANYXX_AGENT_ID</c>) never received a token — memory
    /// signs only those — so it is skipped rather than refused.
    /// </summary>
    public async Task<Outcome<bool>?> RevokeAsync(string? agentId, CancellationToken ct)
    {
        if (!On || !CouldHoldToken(agentId))
            return null;
        var revoked = await mediator.Send(new RevokeSandboxMemoryTokenCommand(agentId!), ct);
        return revoked.Status == OutcomeStatus.Ok ? null : revoked;
    }

    private static bool CouldHoldToken(string? agentId) =>
        Identifier.IsValid(agentId) && agentId!.StartsWith("ax-", StringComparison.Ordinal);
}
