namespace Skanyxx.Core.Platform.Studio;

/// <param name="Problems">Why it cannot be merged as it is (empty = valid). Checked again at merge.</param>
/// <param name="NeedsOwner">
/// Only the owner may merge it: it sets or removes a team or department grant (D091), or brings a plugin from git or a zip
/// (D222).
/// </param>
public sealed record ProposalDetail(
    ProposalSummary Summary, IReadOnlyList<ProposalFile> Files, IReadOnlyList<string> Problems, bool NeedsOwner, PreviewAgent? Preview)
{
    /// <summary>What the reviewer should know that does not stop the merge (e.g. a server whose stored values stay with its old host).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
