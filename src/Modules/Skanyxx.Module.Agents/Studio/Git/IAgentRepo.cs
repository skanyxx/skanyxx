namespace Skanyxx.Module.Agents.Studio.Git;

/// <summary>
/// The agent-config repo (D023): the bundled Gitea today; a customer's GitHub/GitLab is another implementation of this
/// interface (an owner option, not this slice). Every failure to reach or understand git is an
/// <see cref="HttpRequestException"/> with a fixed message; the token never appears in one. Every answer is read with a
/// limit, never buffered whole.
/// </summary>
internal interface IAgentRepo
{
    /// <summary>The largest agent file read (D111 M1): bigger is refused before it is read in full.</summary>
    const int MaxFileBytes = 64 * 1024;

    /// <summary><c>owner/repo</c>.</summary>
    string Name { get; }

    /// <summary>The repo as it is now, or null when git has none by that name.</summary>
    Task<RepoInfo?> InfoAsync(CancellationToken ct);

    /// <summary>Org and repo (<c>main</c>, initial commit); each step tolerates "exists". Only before a repo was ever recorded (D119).</summary>
    Task<RepoInfo> CreateAsync(CancellationToken ct);

    /// <summary>
    /// Creates the main protection rule when it is missing, then <see cref="VerifyAsync"/>. Only where the studio takes a
    /// repo on (its creation, first sight, the owner's confirmation; D119), never for a recorded one.
    /// </summary>
    Task ProtectAsync(RepoInfo repo, CancellationToken ct);

    /// <summary>
    /// Checks, without writing, that the repo is private and main takes merges only, by Skanyxx's account (main being
    /// protected is what makes it trusted, D112).
    /// </summary>
    /// <exception cref="Reconcile.StudioRepoException">It is not, or the rule is missing.</exception>
    Task VerifyAsync(RepoInfo repo, CancellationToken ct);

    /// <summary>One commit with every change on a new branch from main, then a pull request into main.</summary>
    Task<RepoPull> ProposeAsync(string branch, IReadOnlyList<RepoChange> changes, string message, string authorName, string authorEmail,
        string title, string body, CancellationToken ct);

    /// <summary>Every open pull request, page by page.</summary>
    Task<IReadOnlyList<RepoPull>> OpenPullsAsync(CancellationToken ct);

    Task<RepoPull?> PullAsync(int number, CancellationToken ct);

    /// <summary>The first 50 changed files (a valid proposal changes two).</summary>
    Task<IReadOnlyList<RepoFileChange>> PullFilesAsync(int number, CancellationToken ct);

    /// <summary>The file at a branch or commit, or null when it is not there.</summary>
    /// <exception cref="RepoFileTooLargeException">Bigger than <see cref="MaxFileBytes"/>.</exception>
    Task<string?> ReadAsync(string path, string reference, CancellationToken ct);

    /// <summary>Every file path in main, page by page.</summary>
    Task<IReadOnlyList<string>> ListMainAsync(CancellationToken ct);

    /// <summary>Merges only if the branch is still at <paramref name="headSha"/> (what was validated).</summary>
    Task<MergeOutcome> MergeAsync(int number, string headSha, string title, string message, CancellationToken ct);

    /// <summary>Closes without merging and deletes its branch.</summary>
    Task CloseAsync(RepoPull pull, CancellationToken ct);
}
