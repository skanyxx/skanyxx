using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Git;

/// <summary>
/// Gitea's REST API (research/gitea-api.md), with the one account Skanyxx holds. The token rides in the HttpClient's
/// default header (set in the module), never in a URL or a log line; Gitea's answers are logged cut, at Warning.
/// </summary>
internal sealed class GiteaAgentRepo(IHttpClientFactory clients, IOptions<StudioOptions> options, ILogger<GiteaAgentRepo> logger) : IAgentRepo
{
    public const string HttpClientName = "studio-git";

    /// <summary>Gitea's default <c>MAX_RESPONSE_ITEMS</c>: a full page means there may be another.</summary>
    private const int PageSize = 50;

    /// <summary>Gitea's default <c>DEFAULT_GIT_TREES_PER_PAGE</c>.</summary>
    private const int TreePageSize = 1000;

    /// <summary>At most 2,000 open proposals and 40,000 files in main; past that git is not answering about an agent repo.</summary>
    private const int MaxPages = 40;

    private const int MaxJsonBytes = 8 * 1024 * 1024;

    private readonly StudioOptions.GitOptions _git = options.Value.Git;

    public string Name => $"{_git.Owner}/{_git.Repo}";

    private string RepoPath => $"repos/{Uri.EscapeDataString(_git.Owner)}/{Uri.EscapeDataString(_git.Repo)}";

    public async Task<RepoInfo?> InfoAsync(CancellationToken ct) =>
        await SendAsync(HttpMethod.Get, RepoPath, null, ct, HttpStatusCode.NotFound) is { } repo ? Info(repo) : null;

    public async Task<RepoInfo> CreateAsync(CancellationToken ct)
    {
        if (await SendAsync(HttpMethod.Get, $"orgs/{Uri.EscapeDataString(_git.Owner)}", null, ct, HttpStatusCode.NotFound) is null)
            await SendAsync(HttpMethod.Post, "orgs", new JsonObject { ["username"] = _git.Owner, ["visibility"] = "private" },
                ct, HttpStatusCode.UnprocessableEntity);
        await SendAsync(HttpMethod.Post, $"orgs/{Uri.EscapeDataString(_git.Owner)}/repos", new JsonObject
        {
            ["name"] = _git.Repo,
            ["description"] = "Skanyxx agents: one folder per agent (kagent Agent + memory grants). Changed only by pull requests merged in Skanyxx.",
            ["private"] = true,
            ["auto_init"] = true,
            ["readme"] = "Default",
            ["default_branch"] = _git.Branch
        }, ct, HttpStatusCode.Conflict);
        logger.LogWarning("Created the agent repo {Repo} in git", Name);
        return await InfoAsync(ct) ?? throw new HttpRequestException("git does not show the agent repo it just created");
    }

    public async Task ProtectAsync(RepoInfo repo, CancellationToken ct)
    {
        if (await RuleAsync(ct) is null)
        {
            // No direct writes to main for anyone (verified for a site admin too), and only Skanyxx's account may merge.
            // 403 is Gitea's "already exists": a racing creator, which the check below then judges like any other.
            await SendAsync(HttpMethod.Post, $"{RepoPath}/branch_protections", new JsonObject
            {
                ["rule_name"] = _git.Branch,
                ["enable_push"] = false,
                ["enable_force_push"] = false,
                ["enable_merge_whitelist"] = true,
                ["merge_whitelist_usernames"] = new JsonArray(_git.User),
                ["block_admin_merge_override"] = true
            }, ct, HttpStatusCode.Forbidden);
            logger.LogWarning("Protected {Branch} of the agent repo {Repo}: merges only, by {User}", _git.Branch, Name, _git.User);
        }
        await VerifyAsync(repo, ct);
    }

    public async Task VerifyAsync(RepoInfo repo, CancellationToken ct)
    {
        if (!repo.Private)
            throw new StudioRepoException($"The agent repo {Name} is not private; the studio changes nothing until it is.");
        var rule = await RuleAsync(ct);
        var mergers = (rule?["merge_whitelist_usernames"] as JsonArray ?? []).Select(Text).ToList();
        if (rule is null || Bool(rule["enable_push"]) != false || Bool(rule["enable_merge_whitelist"]) != true || mergers is not [var only] || only != _git.User)
            throw new StudioRepoException($"{_git.Branch} of the agent repo {Name} is not protected as the studio requires (no direct pushes, merges only by {_git.User}); "
                + $"the studio changes nothing until it is. {StudioRepoGuard.ConfirmHint}");
    }

    private async Task<JsonNode?> RuleAsync(CancellationToken ct) =>
        (await SendAsync(HttpMethod.Get, $"{RepoPath}/branch_protections", null, ct) as JsonArray ?? []).FirstOrDefault(r => Text(r?["rule_name"]) == _git.Branch);

    public async Task<RepoPull> ProposeAsync(string branch, IReadOnlyList<RepoChange> changes, string message, string authorName, string authorEmail,
        string title, string body, CancellationToken ct)
    {
        await SendAsync(HttpMethod.Post, $"{RepoPath}/contents", new JsonObject
        {
            ["branch"] = _git.Branch,
            ["new_branch"] = branch,
            ["message"] = message,
            ["author"] = new JsonObject { ["name"] = authorName, ["email"] = authorEmail },
            ["files"] = new JsonArray([.. changes.Select(c => (JsonNode)new JsonObject
            {
                ["operation"] = c.Exists ? "update" : "create",
                ["path"] = c.Path,
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(c.Content))
            })])
        }, ct);
        var pull = await SendAsync(HttpMethod.Post, $"{RepoPath}/pulls",
            new JsonObject { ["head"] = branch, ["base"] = _git.Branch, ["title"] = title, ["body"] = body }, ct);
        return Pull(pull);
    }

    public async Task<IReadOnlyList<RepoPull>> OpenPullsAsync(CancellationToken ct)
    {
        var pulls = new List<RepoPull>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await SendAsync(HttpMethod.Get, $"{RepoPath}/pulls?state=open&page={page}&limit={PageSize}", null, ct) as JsonArray ?? [];
            pulls.AddRange(batch.Select(Pull));
            if (batch.Count < PageSize)
                return pulls;
        }
        throw new HttpRequestException($"git lists more than {MaxPages * PageSize} open pull requests");
    }

    public async Task<RepoPull?> PullAsync(int number, CancellationToken ct) =>
        await SendAsync(HttpMethod.Get, $"{RepoPath}/pulls/{number}", null, ct, HttpStatusCode.NotFound) is { } pull ? Pull(pull) : null;

    public async Task<IReadOnlyList<RepoFileChange>> PullFilesAsync(int number, CancellationToken ct) =>
        [.. (await SendAsync(HttpMethod.Get, $"{RepoPath}/pulls/{number}/files?limit={PageSize}", null, ct) as JsonArray ?? [])
            .Select(f => new RepoFileChange(Text(f?["filename"]) ?? "", Text(f?["status"]) ?? ""))];

    public async Task<string?> ReadAsync(string path, string reference, CancellationToken ct)
    {
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var (status, text) = await CallAsync(HttpMethod.Get, $"{RepoPath}/raw/{escaped}?ref={Uri.EscapeDataString(reference)}", null, IAgentRepo.MaxFileBytes, ct);
        if (status == HttpStatusCode.NotFound)
            return null;
        EnsureSuccess(status, text, $"read {path}");
        return text;
    }

    public async Task<IReadOnlyList<string>> ListMainAsync(CancellationToken ct)
    {
        var paths = new List<string>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var tree = await SendAsync(HttpMethod.Get,
                $"{RepoPath}/git/trees/{Uri.EscapeDataString(_git.Branch)}?recursive=true&page={page}&per_page={TreePageSize}", null, ct);
            paths.AddRange((tree?["tree"] as JsonArray ?? []).Where(e => Text(e?["type"]) == "blob").Select(e => Text(e?["path"])).OfType<string>());
            // Gitea pages a tree: truncated means another page follows.
            if (Bool(tree?["truncated"]) != true)
                return paths;
        }
        throw new HttpRequestException($"git lists more than {MaxPages * TreePageSize} files in main");
    }

    public async Task<MergeOutcome> MergeAsync(int number, string headSha, string title, string message, CancellationToken ct)
    {
        var (status, text) = await CallAsync(HttpMethod.Post, $"{RepoPath}/pulls/{number}/merge", new JsonObject
        {
            ["Do"] = "merge",
            ["head_commit_id"] = headSha,
            ["MergeTitleField"] = title,
            ["MergeMessageField"] = message,
            ["delete_branch_after_merge"] = true
        }, MaxJsonBytes, ct);
        switch (status)
        {
            case HttpStatusCode.Conflict when text.Contains("head out of date", StringComparison.OrdinalIgnoreCase):
                return MergeOutcome.HeadMoved;
            case HttpStatusCode.Conflict or HttpStatusCode.MethodNotAllowed:
                logger.LogWarning("git refused to merge #{Number} of {Repo}: {Status}", number, Name, (int)status);
                return MergeOutcome.NotMergeable;
            default:
                EnsureSuccess(status, text, $"merge #{number}");
                return MergeOutcome.Merged;
        }
    }

    public async Task CloseAsync(RepoPull pull, CancellationToken ct)
    {
        await SendAsync(HttpMethod.Patch, $"{RepoPath}/pulls/{pull.Number}", new JsonObject { ["state"] = "closed" }, ct);
        await SendAsync(HttpMethod.Delete, $"{RepoPath}/branches/{Uri.EscapeDataString(pull.HeadRef)}", null, ct, HttpStatusCode.NotFound);
    }

    private RepoInfo Info(JsonNode repo) => new(
        new StudioRepoIdentity(
            repo["id"] is JsonValue id && id.TryGetValue<long>(out var value) ? value : throw new HttpRequestException("git answered a repo without an id"),
            Text(repo["created_at"]) ?? throw new HttpRequestException("git answered a repo without created_at"),
            Text(repo["full_name"]) ?? Name),
        Bool(repo["private"]) == true);

    private static RepoPull Pull(JsonNode? pull) => new(
        pull?["number"] is JsonValue n && n.TryGetValue<int>(out var number) ? number : throw new HttpRequestException("git answered a pull request without a number"),
        Text(pull["title"]) ?? "",
        Text(pull["body"]) ?? "",
        Text(pull["base"]?["ref"]) ?? "",
        Text(pull["head"]?["ref"]) ?? "",
        Text(pull["head"]?["sha"]) ?? "",
        Text(pull["state"]) == "open",
        Bool(pull["merged"]) == true,
        Bool(pull["mergeable"]) == true);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool? Bool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;

    /// <summary>
    /// The parsed answer, or null when git answered one of <paramref name="tolerated"/> (absent, already there). Anything
    /// else that is not a success throws.
    /// </summary>
    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct, params HttpStatusCode[] tolerated)
    {
        string text;
        HttpStatusCode status;
        try
        {
            (status, text) = await CallAsync(method, path, body, MaxJsonBytes, ct);
        }
        catch (RepoFileTooLargeException ex)
        {
            throw new HttpRequestException("git answered more than the studio reads", ex, HttpStatusCode.BadGateway);
        }
        if (tolerated.Contains(status))
            return null;
        EnsureSuccess(status, text, $"{method} {path.Split('?')[0]}");
        try
        {
            return text.Length == 0 ? new JsonObject() : JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new HttpRequestException("git answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }
    }

    /// <summary>One call within <see cref="StudioOptions.GitOptions.TimeoutSeconds"/>, its answer read up to <paramref name="maxBytes"/>.</summary>
    private async Task<(HttpStatusCode Status, string Text)> CallAsync(HttpMethod method, string path, JsonObject? body, int maxBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_git.TimeoutSeconds));
        try
        {
            using var response = await clients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new RepoFileTooLargeException(maxBytes);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    throw new RepoFileTooLargeException(maxBytes);
                buffer.Write(chunk, 0, read);
            }
            return (response.StatusCode, Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"git did not answer within {_git.TimeoutSeconds} s.");
        }
    }

    private void EnsureSuccess(HttpStatusCode status, string text, string what)
    {
        if ((int)status is >= 200 and < 300)
            return;
        logger.LogWarning("git {What} on {Repo} answered {Status}: {Content}", what, Name, (int)status, text.Length <= 500 ? text : text[..500]);
        throw new HttpRequestException($"git answered HTTP {(int)status}", null, status);
    }
}
