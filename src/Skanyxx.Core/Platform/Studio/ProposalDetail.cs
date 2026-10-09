namespace Skanyxx.Core.Platform.Studio;

/// <param name="Problems">Why it cannot be merged as it is (empty = valid). Checked again at merge.</param>
/// <param name="NeedsOwner">It sets or removes a team or department grant, which only the owner may merge (D091).</param>
public sealed record ProposalDetail(
    ProposalSummary Summary, IReadOnlyList<ProposalFile> Files, IReadOnlyList<string> Problems, bool NeedsOwner, PreviewAgent? Preview);
