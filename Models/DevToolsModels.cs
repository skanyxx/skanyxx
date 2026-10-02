using System.Text.Json.Serialization;

namespace SkanyxxWeb.Models;

// ── DB Entities ──────────────────────────────────────────────────────────────

public class DevToolsLlmConnection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "claude"; // claude | ollama | openai | custom
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string? ApiKey { get; set; }
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DevToolsLlmConnectionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "claude";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string? ApiKey { get; set; }
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class DevToolsAdminGlobalDto
{
    // Jira
    public string? JiraUrl { get; set; }
    public string? JiraEmail { get; set; }
    public string? JiraToken { get; set; }
    public string? JiraProjectKey { get; set; }
    // Monday
    public string? MondayToken { get; set; }
    public string? MondayBoardId { get; set; }
    // Linear
    public string? LinearToken { get; set; }
    public string? LinearTeamKey { get; set; }
    // GitHub Issues
    public string? GitHubToken { get; set; }
    public string? GitHubOwner { get; set; }
    public string? GitHubRepo { get; set; }
    // GitLab Issues
    public string? GitLabUrl { get; set; }
    public string? GitLabToken { get; set; }
    public string? GitLabProjectId { get; set; }
    // Azure DevOps
    public string? AzureDevOpsOrg { get; set; }
    public string? AzureDevOpsProject { get; set; }
    public string? AzureDevOpsPat { get; set; }
    // Asana
    public string? AsanaToken { get; set; }
    public string? AsanaProjectId { get; set; }
    // ClickUp
    public string? ClickUpToken { get; set; }
    public string? ClickUpListId { get; set; }
    // YouTrack
    public string? YouTrackUrl { get; set; }
    public string? YouTrackToken { get; set; }
    public string? YouTrackProject { get; set; }
    // Trello
    public string? TrelloKey { get; set; }
    public string? TrelloToken { get; set; }
    public string? TrelloBoardId { get; set; }
    // Shortcut (Clubhouse)
    public string? ShortcutToken { get; set; }
    public string? ShortcutWorkflowStateId { get; set; }
    // Notion
    public string? NotionToken { get; set; }
    public string? NotionDatabaseId { get; set; }
}

public class DevToolsTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class DevToolsSkill
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SystemPromptAddition { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DevToolsSkillDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SystemPromptAddition { get; set; } = "";
    public bool IsBuiltIn { get; set; }
}

public class DevToolsWorkspace
{
    public int Id { get; set; }
    public string Name { get; set; } = "Workspace 1";
    public int SortOrder { get; set; }
    public string LlmProvider { get; set; } = "claude"; // claude | ollama | openai | custom
    public string LlmUrl { get; set; } = "";
    public string LlmModel { get; set; } = "claude-sonnet-4-6";
    public string? ApiKey { get; set; }
    public string EnabledSkillsJson { get; set; } = "[]";
    public string? JiraUrl { get; set; }
    public string? JiraEmail { get; set; }
    public string? JiraToken { get; set; }
    public string? JiraProjectKey { get; set; }
    public string? MondayToken { get; set; }
    public string? MondayBoardId { get; set; }
    public string? GitRepoPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DevToolsSession
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string TaskId { get; set; } = "";
    public string TaskSource { get; set; } = "manual";
    public string TaskTitle { get; set; } = "";
    public string TaskDescription { get; set; } = "";
    public string MessagesJson { get; set; } = "[]";
    public string? SummaryContext { get; set; }
    public int TotalTokensUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── DTOs / API models ─────────────────────────────────────────────────────────

public class DevToolsWorkspaceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public string LlmProvider { get; set; } = "claude";
    public string LlmUrl { get; set; } = "";
    public string LlmModel { get; set; } = "claude-sonnet-4-6";
    public string? ApiKey { get; set; }
    public List<string> EnabledSkills { get; set; } = new();
    public string? JiraUrl { get; set; }
    public string? JiraEmail { get; set; }
    public string? JiraToken { get; set; }
    public string? JiraProjectKey { get; set; }
    public string? MondayToken { get; set; }
    public string? MondayBoardId { get; set; }
    public string? GitRepoPath { get; set; }
}

public class DevToolsTaskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string Type { get; set; } = "task";
    public string Priority { get; set; } = "";
    public string? Url { get; set; }
    public string Source { get; set; } = "";
}

public class DevToolsChatRequest
{
    public int WorkspaceId { get; set; }
    public string TaskId { get; set; } = "";
    public string TaskSource { get; set; } = "manual";
    public string TaskTitle { get; set; } = "";
    public string TaskDescription { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>Optional images attached to this message (base64 data URIs)</summary>
    public List<string>? Images { get; set; }
    /// <summary>Client-generated ID echoed back on every hub event so the frontend
    /// can route tokens to the correct chat bubble when multiple tabs are active.</summary>
    public string CorrelationId { get; set; } = "";
}

public class DevToolsChatResponse
{
    public string Content { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalSessionTokens { get; set; }
    public string Error { get; set; } = "";
}

public class DevToolsSessionDto
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string TaskId { get; set; } = "";
    public string TaskSource { get; set; } = "";
    public string TaskTitle { get; set; } = "";
    public string TaskDescription { get; set; } = "";
    public List<DevToolsChatMessage> Messages { get; set; } = new();
    public string? SummaryContext { get; set; }
    public int TotalTokensUsed { get; set; }
}

public class DevToolsChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
    /// <summary>Base64 data URIs, e.g. "data:image/png;base64,..."</summary>
    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("tokens")]
    public int Tokens { get; set; }
}

public class DevToolsAgentActionRequest
{
    public int WorkspaceId { get; set; }
    public string TaskId { get; set; } = "";
    public string TaskTitle { get; set; } = "";
    public string TaskDescription { get; set; } = "";
    public string ActionType { get; set; } = ""; // unit-tests | integration-tests | system-tests | qa | git-comment | jira-comment | branch
    public string? BaseBranch { get; set; } = "main";
    public string? BranchName { get; set; }
    public string? PrNumber { get; set; }
    public string? AdditionalContext { get; set; }
}

public class DevToolsActionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string? Error { get; set; }
    public string ActionType { get; set; } = "";
}

// ── Hybrid Mode ───────────────────────────────────────────────────────────────

public class HybridChatRequest
{
    /// <summary>IDs of DevToolsLlmConnections to broadcast to.</summary>
    public List<int> ConnectionIds { get; set; } = new();
    public string Message { get; set; } = "";
    public List<string>? Images { get; set; }
    /// <summary>Optional system prompt override. Falls back to default if empty.</summary>
    public string? SystemPrompt { get; set; }
}

public class HybridLaneResult
{
    public int ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Reply { get; set; } = "";
    public string? Error { get; set; }
    public int Ms { get; set; }
}

public class HybridReviewRequest
{
    /// <summary>Connection ID of the LLM that will act as reviewer.</summary>
    public int ReviewerConnectionId { get; set; }
    public string OriginalPrompt { get; set; } = "";
    public string ContentToReview { get; set; } = "";
    /// <summary>Name/label of the LLM that produced the content.</summary>
    public string AuthorLabel { get; set; } = "";
}

public class HybridReviewResult
{
    public string Review { get; set; } = "";
    public string? Error { get; set; }
}

public class HybridFixRequest
{
    /// <summary>Connection ID of the original author LLM that will fix its output.</summary>
    public int AuthorConnectionId { get; set; }
    public string OriginalPrompt { get; set; } = "";
    public string OriginalReply { get; set; } = "";
    public string ReviewFeedback { get; set; } = "";
}

public class HybridFixResult
{
    public string Reply { get; set; } = "";
    public string? Error { get; set; }
}
