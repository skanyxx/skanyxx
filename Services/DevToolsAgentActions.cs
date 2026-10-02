using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

/// <summary>Generates or executes all Dev Tools agent actions (branch, tests, QA, git comment, Jira comment).</summary>
internal sealed class DevToolsAgentActions
{
    private readonly DevToolsLlmClient _llm;
    private readonly IHttpClientFactory _http;
    private readonly ILogger _log;

    public DevToolsAgentActions(DevToolsLlmClient llm, IHttpClientFactory http, ILogger log)
    {
        _llm  = llm;
        _http = http;
        _log  = log;
    }

    public Task<DevToolsActionResult> ExecuteAsync(DevToolsWorkspace ws, DevToolsAgentActionRequest req) =>
        req.ActionType switch
        {
            "branch"             => CreateBranchAsync(ws, req),
            "unit-tests"         => RunTestAgentAsync(ws, req, "unit"),
            "integration-tests"  => RunTestAgentAsync(ws, req, "integration"),
            "system-tests"       => RunTestAgentAsync(ws, req, "system"),
            "qa"                 => RunQaAgentAsync(ws, req),
            "git-comment"        => GenerateGitCommentAsync(ws, req),
            "jira-comment"       => PostJiraCommentAsync(ws, req),
            _                    => Task.FromResult(new DevToolsActionResult { Error = "Unknown action type.", ActionType = req.ActionType })
        };

    // ── Branch ────────────────────────────────────────────────────────────────

    private async Task<DevToolsActionResult> CreateBranchAsync(DevToolsWorkspace ws, DevToolsAgentActionRequest req)
    {
        var branchName = req.BranchName;
        if (string.IsNullOrEmpty(branchName))
        {
            var prompt = $"Generate a git branch name for task [{req.TaskId}]: {req.TaskTitle}. " +
                         "Format: feat/TASK-ID-short-description or fix/TASK-ID-short-description. " +
                         "Return ONLY the branch name, nothing else.";
            var msgs   = new List<DevToolsChatMessage> { new() { Role = "user", Content = prompt } };
            var (name, _, _) = await _llm.CallAsync(ws, "You generate concise git branch names.", msgs);
            branchName = name.Trim().Split('\n')[0].Trim('`').Trim();
        }
        try
        {
            var repoPath   = !string.IsNullOrEmpty(ws.GitRepoPath) ? ws.GitRepoPath : ".";
            var baseBranch = req.BaseBranch ?? "main";
            var result     = await RunGitCommandAsync(repoPath, $"checkout -b {branchName} {baseBranch}");
            return new DevToolsActionResult
            {
                Success    = result.exitCode == 0,
                Output     = result.exitCode == 0 ? $"Branch created: {branchName}" : result.output,
                Error      = result.exitCode != 0 ? result.output : null,
                ActionType = "branch"
            };
        }
        catch (Exception ex) { return new DevToolsActionResult { Error = ex.Message, ActionType = "branch" }; }
    }

    // ── Test generation ───────────────────────────────────────────────────────

    private async Task<DevToolsActionResult> RunTestAgentAsync(
        DevToolsWorkspace ws, DevToolsAgentActionRequest req, string testType)
    {
        var typeLabel = testType switch
        {
            "unit"        => "unit tests",
            "integration" => "integration tests",
            "system"      => "system/end-to-end tests",
            _             => "tests"
        };
        var prompt = $"""
            Task: [{req.TaskId}] {req.TaskTitle}
            Description: {req.TaskDescription}
            {(string.IsNullOrEmpty(req.AdditionalContext) ? "" : $"Additional context: {req.AdditionalContext}")}

            Generate comprehensive {typeLabel} for this task. Include:
            1. Test class structure with descriptive test names
            2. Test cases covering happy path, edge cases, and error scenarios
            3. Mock/stub setup where needed
            4. Assertions with clear failure messages

            Use the project's existing patterns and frameworks. Return complete, runnable test code.
            """;
        var msgs = new List<DevToolsChatMessage> { new() { Role = "user", Content = prompt } };
        var (output, _, _) = await _llm.CallAsync(ws, $"You are a senior QA engineer specializing in {testType} testing. Write production-quality test code.", msgs);
        return new DevToolsActionResult { Success = true, Output = output, ActionType = $"{testType}-tests" };
    }

    // ── QA Review ─────────────────────────────────────────────────────────────

    private async Task<DevToolsActionResult> RunQaAgentAsync(DevToolsWorkspace ws, DevToolsAgentActionRequest req)
    {
        var prompt = $"""
            Task: [{req.TaskId}] {req.TaskTitle}
            Description: {req.TaskDescription}
            {(string.IsNullOrEmpty(req.AdditionalContext) ? "" : $"Code/Context:\n{req.AdditionalContext}")}

            Perform a comprehensive QA review covering:
            1. Functional correctness - does it meet requirements?
            2. Edge cases and boundary conditions
            3. Security vulnerabilities (OWASP Top 10)
            4. Performance concerns
            5. Code quality and maintainability
            6. Test coverage gaps
            7. Suggested test scenarios

            Format as a structured QA report with severity ratings (Critical/High/Medium/Low).
            """;
        var msgs = new List<DevToolsChatMessage> { new() { Role = "user", Content = prompt } };
        var (output, _, _) = await _llm.CallAsync(ws, "You are a senior QA engineer. Be thorough and precise.", msgs);
        return new DevToolsActionResult { Success = true, Output = output, ActionType = "qa" };
    }

    // ── Git comment ───────────────────────────────────────────────────────────

    private async Task<DevToolsActionResult> GenerateGitCommentAsync(DevToolsWorkspace ws, DevToolsAgentActionRequest req)
    {
        var prompt = $"""
            Task: [{req.TaskId}] {req.TaskTitle}
            {(string.IsNullOrEmpty(req.AdditionalContext) ? "" : $"PR/Code context:\n{req.AdditionalContext}")}

            Generate a professional, constructive code review comment for this PR/commit.
            Focus on: code quality, potential bugs, performance, security, and suggestions.
            Format as markdown suitable for GitHub/GitLab PR comments.
            """;
        var msgs = new List<DevToolsChatMessage> { new() { Role = "user", Content = prompt } };
        var (output, _, _) = await _llm.CallAsync(ws, "You are a senior code reviewer. Be constructive and precise.", msgs);
        return new DevToolsActionResult { Success = true, Output = output, ActionType = "git-comment" };
    }

    // ── Jira comment ──────────────────────────────────────────────────────────

    private async Task<DevToolsActionResult> PostJiraCommentAsync(DevToolsWorkspace ws, DevToolsAgentActionRequest req)
    {
        var prompt = $"""
            Task: [{req.TaskId}] {req.TaskTitle}
            Description: {req.TaskDescription}
            {(string.IsNullOrEmpty(req.AdditionalContext) ? "" : $"Context:\n{req.AdditionalContext}")}

            Write a concise, professional Jira comment summarizing:
            1. What was done / findings
            2. Any blockers or dependencies
            3. Next steps
            Keep it under 200 words.
            """;
        var msgs = new List<DevToolsChatMessage> { new() { Role = "user", Content = prompt } };
        var (commentText, _, _) = await _llm.CallAsync(ws, "You write concise, professional Jira comments.", msgs);

        if (!string.IsNullOrEmpty(ws.JiraUrl) && !string.IsNullOrEmpty(ws.JiraEmail) && !string.IsNullOrEmpty(ws.JiraToken))
        {
            try
            {
                var http  = _http.CreateClient();
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ws.JiraEmail}:{ws.JiraToken}"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
                var body = new
                {
                    body = new
                    {
                        type = "doc", version = 1,
                        content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = commentText } } } }
                    }
                };
                var url  = $"{ws.JiraUrl.TrimEnd('/')}/rest/api/3/issue/{req.TaskId}/comment";
                var resp = await http.PostAsync(url,
                    new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
                if (resp.IsSuccessStatusCode)
                    return new DevToolsActionResult { Success = true, Output = $"Comment posted to {req.TaskId}:\n\n{commentText}", ActionType = "jira-comment" };
                var err = await resp.Content.ReadAsStringAsync();
                return new DevToolsActionResult { Success = false, Output = commentText, Error = $"Jira error {resp.StatusCode}: {err}", ActionType = "jira-comment" };
            }
            catch (Exception ex) { return new DevToolsActionResult { Success = false, Output = commentText, Error = ex.Message, ActionType = "jira-comment" }; }
        }
        return new DevToolsActionResult { Success = true, Output = commentText, ActionType = "jira-comment" };
    }

    // ── Git process helper ────────────────────────────────────────────────────

    private static async Task<(int exitCode, string output)> RunGitCommandAsync(string repoPath, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory    = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute     = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, string.IsNullOrEmpty(stdout) ? stderr : stdout);
    }
}
