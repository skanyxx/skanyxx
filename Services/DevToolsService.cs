using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SkanyxxWeb.Data;
using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class DevToolsService : IDevToolsService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly DevToolsLlmClient _llm;
    private readonly DevToolsTaskFetcher _fetcher;
    private readonly DevToolsAgentActions _actions;
    private readonly DevToolsSettingsRepository _settings;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int TokenCompressThreshold = 6000;

    public DevToolsService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHttpClientFactory httpFactory,
        DevToolsLlmClient llm,
        ILogger<DevToolsService> log)
    {
        _dbFactory = dbFactory;
        _llm       = llm;
        _fetcher   = new DevToolsTaskFetcher(httpFactory, log);
        _actions   = new DevToolsAgentActions(llm, httpFactory, log);
        _settings  = new DevToolsSettingsRepository(dbFactory, llm, _fetcher);
    }

    // ── Workspaces ────────────────────────────────────────────────────────────

    public async Task<List<DevToolsWorkspaceDto>> GetWorkspacesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.DevToolsWorkspaces.OrderBy(w => w.SortOrder).Select(e => ToDto(e)).ToListAsync();
    }

    public async Task<DevToolsWorkspaceDto> CreateWorkspaceAsync(DevToolsWorkspaceDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var count = await db.DevToolsWorkspaces.CountAsync();
        if (count >= 10) throw new InvalidOperationException("Maximum of 10 workspaces reached.");
        var entity = FromDto(dto);
        entity.SortOrder = count;
        entity.CreatedAt = entity.UpdatedAt = DateTime.UtcNow;
        db.DevToolsWorkspaces.Add(entity);
        await db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<DevToolsWorkspaceDto?> UpdateWorkspaceAsync(int id, DevToolsWorkspaceDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.DevToolsWorkspaces.FindAsync(id);
        if (entity is null) return null;
        entity.Name            = dto.Name;
        entity.LlmProvider     = dto.LlmProvider;
        entity.LlmUrl          = dto.LlmUrl;
        entity.LlmModel        = dto.LlmModel;
        entity.EnabledSkillsJson = JsonSerializer.Serialize(dto.EnabledSkills);
        entity.JiraUrl         = dto.JiraUrl;
        entity.JiraEmail       = dto.JiraEmail;
        entity.JiraProjectKey  = dto.JiraProjectKey;
        entity.MondayBoardId   = dto.MondayBoardId;
        entity.GitRepoPath     = dto.GitRepoPath;
        if (!string.IsNullOrEmpty(dto.ApiKey)      && dto.ApiKey      != "••••••••") entity.ApiKey      = dto.ApiKey;
        if (!string.IsNullOrEmpty(dto.JiraToken)   && dto.JiraToken   != "••••••••") entity.JiraToken   = dto.JiraToken;
        if (!string.IsNullOrEmpty(dto.MondayToken) && dto.MondayToken != "••••••••") entity.MondayToken = dto.MondayToken;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<bool> DeleteWorkspaceAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.DevToolsWorkspaces.FindAsync(id);
        if (entity is null) return false;
        db.DevToolsWorkspaces.Remove(entity);
        db.DevToolsSessions.RemoveRange(db.DevToolsSessions.Where(s => s.WorkspaceId == id));
        await db.SaveChangesAsync();
        return true;
    }

    // ── Tasks ─────────────────────────────────────────────────────────────────

    public async Task<List<DevToolsTaskItem>> GetTasksAsync(int workspaceId, string source)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var ws = await db.DevToolsWorkspaces.FindAsync(workspaceId);
        if (ws is null) return new();
        var g = await _settings.LoadGlobalSettings(db);
        return await _fetcher.FetchAsync(source, ws, g);
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    public async Task<DevToolsChatResponse> SendMessageAsync(DevToolsChatRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var ws = await db.DevToolsWorkspaces.FindAsync(request.WorkspaceId);
        if (ws is null) return new DevToolsChatResponse { Error = "Workspace not found." };

        var session = await db.DevToolsSessions
            .FirstOrDefaultAsync(s => s.WorkspaceId == request.WorkspaceId && s.TaskId == request.TaskId);
        if (session is null)
        {
            session = new DevToolsSession
            {
                WorkspaceId = request.WorkspaceId, TaskId = request.TaskId,
                TaskSource = request.TaskSource, TaskTitle = request.TaskTitle,
                TaskDescription = request.TaskDescription, CreatedAt = DateTime.UtcNow
            };
            db.DevToolsSessions.Add(session);
        }

        var messages = DeserializeMessages(session.MessagesJson);
        // Inject local file contents referenced in the message before sending to LLM
        var enrichedMessage = InjectLocalFiles(request.Message, ws.GitRepoPath);
        messages.Add(new DevToolsChatMessage { Role = "user", Content = enrichedMessage, Images = request.Images, Timestamp = DateTime.UtcNow });

        var enabledNames  = JsonSerializer.Deserialize<List<string>>(ws.EnabledSkillsJson) ?? new();
        var enabledSkills = await db.DevToolsSkills.Where(s => enabledNames.Contains(s.Name)).ToListAsync();
        var systemPrompt  = DevToolsLlmClient.BuildSystemPrompt(ws, session, request, enabledSkills);

        if (DevToolsLlmClient.EstimateTokens(messages) > TokenCompressThreshold)
            (messages, session.SummaryContext) = await CompressHistoryAsync(ws, messages, session.SummaryContext);

        var (reply, inputTokens, outputTokens) = await _llm.CallAsync(ws, systemPrompt, messages);
        messages.Add(new DevToolsChatMessage { Role = "assistant", Content = reply, Timestamp = DateTime.UtcNow, Tokens = outputTokens });

        session.MessagesJson    = JsonSerializer.Serialize(messages);
        session.TotalTokensUsed += inputTokens + outputTokens;
        session.UpdatedAt       = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return new DevToolsChatResponse
        {
            Content = reply, InputTokens = inputTokens,
            OutputTokens = outputTokens, TotalSessionTokens = session.TotalTokensUsed
        };
    }

    /// <summary>
    /// Scans the user message for file paths, reads them from the local filesystem,
    /// and appends their content so the LLM can see them.
    /// </summary>
    private static string InjectLocalFiles(string message, string? workspaceRoot)
    {
        var injected = new List<(string path, string content)>();
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Match absolute paths:  /Users/foo/bar/File.cs  or  ~/foo/bar
        // Match relative paths that exist under workspaceRoot:  Services/Foo.cs
        var patterns = new[]
        {
            @"(?<![`'""\w])((?:/|~/)[\w.\-/]+\.\w{1,10})(?![`'""\w])",   // absolute
            @"(?<![`'""\w])([\w.\-]+(?:/[\w.\-]+)+\.\w{1,10})(?![`'""\w])" // relative
        };

        var candidates = new List<string>();
        foreach (var pattern in patterns)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(message, pattern))
                candidates.Add(m.Value.Trim());

        foreach (var raw in candidates)
        {
            var path = raw.StartsWith("~/")
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), raw[2..])
                : raw;

            // Try absolute first, then relative to workspace root
            if (!File.Exists(path) && !string.IsNullOrEmpty(workspaceRoot))
                path = Path.Combine(workspaceRoot, raw.TrimStart('/'));

            if (!File.Exists(path) || !seen.Add(path)) continue;

            var info = new FileInfo(path);
            if (info.Length > 150 * 1024) continue; // skip files >150 KB

            try
            {
                var content = File.ReadAllText(path);
                injected.Add((path, content));
            }
            catch { /* unreadable — skip */ }
        }

        if (injected.Count == 0) return message;

        var sb = new System.Text.StringBuilder(message);
        sb.AppendLine("\n\n---\nFile contents read from local filesystem:\n");
        foreach (var (path, content) in injected)
        {
            var ext  = Path.GetExtension(path).TrimStart('.').ToLower();
            var lang = ext switch
            {
                "cs" => "csharp", "ts" => "typescript", "js" => "javascript",
                "py" => "python", "go" => "go", "rs" => "rust", "java" => "java",
                "json" => "json", "html" => "html", "css" => "css",
                "sh" => "bash", "yml" or "yaml" => "yaml", "xml" => "xml",
                "sql" => "sql", "md" => "markdown", "csproj" => "xml",
                _ => ext
            };
            sb.AppendLine($"### `{path}`");
            sb.AppendLine($"```{lang}");
            sb.AppendLine(content);
            sb.AppendLine("```\n");
        }
        return sb.ToString();
    }

    private async Task<(List<DevToolsChatMessage>, string?)> CompressHistoryAsync(
        DevToolsWorkspace ws, List<DevToolsChatMessage> messages, string? existingSummary)
    {
        if (messages.Count < 4) return (messages, existingSummary);
        var toSummarize  = messages.Take(messages.Count - 4).ToList();
        var toKeep       = messages.Skip(messages.Count - 4).ToList();
        var historyText  = string.Join("\n", toSummarize.Select(m => $"{m.Role}: {m.Content}"));
        var prevSummary  = string.IsNullOrEmpty(existingSummary) ? "" : $"Previous summary: {existingSummary}\n\n";
        var prompt       = $"{prevSummary}Summarize this conversation history concisely (max 300 words), preserving key decisions, code, and context:\n\n{historyText}";
        var tempMessages = new List<DevToolsChatMessage> { new() { Role = "user", Content = prompt } };
        var (summary, _, _) = await _llm.CallAsync(ws, "You are a precise summarizer. Output only the summary.", tempMessages);
        return (toKeep, summary);
    }

    // ── Chat (streaming) ─────────────────────────────────────────────────────

    public async Task<DevToolsChatResponse> StreamMessageAsync(
        DevToolsChatRequest request, Func<string, Task> onChunk, Func<string, Task>? onTool = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ws = await db.DevToolsWorkspaces.FindAsync(new object[] { request.WorkspaceId }, ct);
        if (ws is null) return new DevToolsChatResponse { Error = "Workspace not found." };

        var session = await db.DevToolsSessions
            .FirstOrDefaultAsync(s => s.WorkspaceId == request.WorkspaceId && s.TaskId == request.TaskId, ct);
        if (session is null)
        {
            session = new DevToolsSession
            {
                WorkspaceId = request.WorkspaceId, TaskId = request.TaskId,
                TaskSource = request.TaskSource, TaskTitle = request.TaskTitle,
                TaskDescription = request.TaskDescription, CreatedAt = DateTime.UtcNow
            };
            db.DevToolsSessions.Add(session);
        }

        var messages       = DeserializeMessages(session.MessagesJson);
        var enrichedMessage = InjectLocalFiles(request.Message, ws.GitRepoPath);
        messages.Add(new DevToolsChatMessage { Role = "user", Content = enrichedMessage, Images = request.Images, Timestamp = DateTime.UtcNow });

        var enabledNames  = JsonSerializer.Deserialize<List<string>>(ws.EnabledSkillsJson) ?? new();
        var enabledSkills = await db.DevToolsSkills.Where(s => enabledNames.Contains(s.Name)).ToListAsync(ct);
        var systemPrompt  = DevToolsLlmClient.BuildSystemPrompt(ws, session, request, enabledSkills);

        if (DevToolsLlmClient.EstimateTokens(messages) > TokenCompressThreshold)
            (messages, session.SummaryContext) = await CompressHistoryAsync(ws, messages, session.SummaryContext);

        var inputTokens = DevToolsLlmClient.EstimateTokens(messages);
        var sb = new System.Text.StringBuilder();

        try
        {
            await foreach (var chunk in _llm.StreamAsync(ws, systemPrompt, messages, onTool, ct).ConfigureAwait(false))
            {
                sb.Append(chunk);
                await onChunk(chunk).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* client disconnected — still save what arrived */ }

        var reply        = sb.ToString();
        var outputTokens = DevToolsLlmClient.EstimateTokens(reply);

        messages.Add(new DevToolsChatMessage { Role = "assistant", Content = reply, Timestamp = DateTime.UtcNow, Tokens = outputTokens });
        session.MessagesJson    = JsonSerializer.Serialize(messages);
        session.TotalTokensUsed += inputTokens + outputTokens;
        session.UpdatedAt       = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None); // save even if client disconnected

        return new DevToolsChatResponse
        {
            Content = reply, InputTokens = inputTokens,
            OutputTokens = outputTokens, TotalSessionTokens = session.TotalTokensUsed
        };
    }

    // ── Sessions ──────────────────────────────────────────────────────────────

    public async Task<DevToolsSessionDto?> GetSessionAsync(int workspaceId, string taskId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.DevToolsSessions.FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId && s.TaskId == taskId);
        return session is null ? null : ToSessionDto(session);
    }

    public async Task<DevToolsSessionDto> SummarizeSessionAsync(int workspaceId, string taskId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.DevToolsSessions.FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId && s.TaskId == taskId);
        if (session is null) return new DevToolsSessionDto();
        var ws = await db.DevToolsWorkspaces.FindAsync(workspaceId);
        if (ws is null) return ToSessionDto(session);
        var messages = DeserializeMessages(session.MessagesJson);
        var (compressed, summary) = await CompressHistoryAsync(ws, messages, session.SummaryContext);
        session.MessagesJson   = JsonSerializer.Serialize(compressed);
        session.SummaryContext = summary;
        session.UpdatedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToSessionDto(session);
    }

    public async Task<bool> DeleteSessionAsync(int workspaceId, string taskId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.DevToolsSessions.FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId && s.TaskId == taskId);
        if (session is null) return false;
        db.DevToolsSessions.Remove(session);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> DeleteAllSessionsAsync(int workspaceId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sessions = db.DevToolsSessions.Where(s => s.WorkspaceId == workspaceId);
        var count    = await sessions.CountAsync();
        db.DevToolsSessions.RemoveRange(sessions);
        await db.SaveChangesAsync();
        return count;
    }

    // ── Agent actions ─────────────────────────────────────────────────────────

    public async Task<DevToolsActionResult> ExecuteAgentActionAsync(DevToolsAgentActionRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var ws = await db.DevToolsWorkspaces.FindAsync(request.WorkspaceId);
        if (ws is null) return new DevToolsActionResult { Error = "Workspace not found.", ActionType = request.ActionType };
        return await _actions.ExecuteAsync(ws, request);
    }

    // ── Skills ────────────────────────────────────────────────────────────────

    public async Task<List<DevToolsSkillDto>> GetSkillsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.DevToolsSkills.OrderBy(s => s.IsBuiltIn ? 0 : 1).ThenBy(s => s.Name)
            .Select(s => new DevToolsSkillDto { Id = s.Id, Name = s.Name, SystemPromptAddition = s.SystemPromptAddition, IsBuiltIn = s.IsBuiltIn })
            .ToListAsync();
    }

    public async Task<DevToolsSkillDto> CreateSkillAsync(DevToolsSkillDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = new DevToolsSkill { Name = dto.Name.Trim(), SystemPromptAddition = dto.SystemPromptAddition.Trim(), IsBuiltIn = false, CreatedAt = DateTime.UtcNow };
        db.DevToolsSkills.Add(entity);
        await db.SaveChangesAsync();
        return new DevToolsSkillDto { Id = entity.Id, Name = entity.Name, SystemPromptAddition = entity.SystemPromptAddition, IsBuiltIn = false };
    }

    public async Task<DevToolsSkillDto?> UpdateSkillAsync(int id, DevToolsSkillDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.DevToolsSkills.FindAsync(id);
        if (entity is null) return null;
        entity.Name = dto.Name.Trim();
        entity.SystemPromptAddition = dto.SystemPromptAddition.Trim();
        await db.SaveChangesAsync();
        return new DevToolsSkillDto { Id = entity.Id, Name = entity.Name, SystemPromptAddition = entity.SystemPromptAddition, IsBuiltIn = entity.IsBuiltIn };
    }

    public async Task<bool> DeleteSkillAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.DevToolsSkills.FindAsync(id);
        if (entity is null || entity.IsBuiltIn) return false;
        db.DevToolsSkills.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Delegate to settings repository ──────────────────────────────────────

    public Task<DevToolsAdminGlobalDto> GetGlobalSettingsAsync()      => _settings.GetGlobalSettingsAsync();
    public Task SaveGlobalSettingsAsync(DevToolsAdminGlobalDto dto)   => _settings.SaveGlobalSettingsAsync(dto);
    public Task<DevToolsTestResult> TestLlmConnectionAsync(int id)    => _settings.TestLlmConnectionAsync(id);
    public Task<DevToolsTestResult> TestTaskSourceAsync(string source) => _settings.TestTaskSourceAsync(source);
    public Task<List<DevToolsLlmConnectionDto>> GetLlmConnectionsAsync()                                      => _settings.GetLlmConnectionsAsync();
    public Task<DevToolsLlmConnectionDto> CreateLlmConnectionAsync(DevToolsLlmConnectionDto dto)              => _settings.CreateLlmConnectionAsync(dto);
    public Task<DevToolsLlmConnectionDto?> UpdateLlmConnectionAsync(int id, DevToolsLlmConnectionDto dto)     => _settings.UpdateLlmConnectionAsync(id, dto);
    public Task<bool> DeleteLlmConnectionAsync(int id)                                                        => _settings.DeleteLlmConnectionAsync(id);

    // ── Static mapping helpers ────────────────────────────────────────────────

    private static DevToolsWorkspaceDto ToDto(DevToolsWorkspace e)
    {
        var skills = new List<string>();
        try { skills = JsonSerializer.Deserialize<List<string>>(e.EnabledSkillsJson) ?? new(); } catch { }
        return new DevToolsWorkspaceDto
        {
            Id = e.Id, Name = e.Name, SortOrder = e.SortOrder,
            LlmProvider = e.LlmProvider, LlmUrl = e.LlmUrl, LlmModel = e.LlmModel,
            ApiKey      = string.IsNullOrEmpty(e.ApiKey)      ? null : "••••••••",
            EnabledSkills = skills,
            JiraUrl = e.JiraUrl, JiraEmail = e.JiraEmail,
            JiraToken   = string.IsNullOrEmpty(e.JiraToken)   ? null : "••••••••",
            JiraProjectKey = e.JiraProjectKey,
            MondayToken = string.IsNullOrEmpty(e.MondayToken) ? null : "••••••••",
            MondayBoardId = e.MondayBoardId, GitRepoPath = e.GitRepoPath
        };
    }

    private static DevToolsWorkspace FromDto(DevToolsWorkspaceDto dto) => new()
    {
        Name = dto.Name, LlmProvider = dto.LlmProvider, LlmUrl = dto.LlmUrl ?? "", LlmModel = dto.LlmModel,
        ApiKey = dto.ApiKey, EnabledSkillsJson = JsonSerializer.Serialize(dto.EnabledSkills),
        JiraUrl = dto.JiraUrl, JiraEmail = dto.JiraEmail, JiraToken = dto.JiraToken,
        JiraProjectKey = dto.JiraProjectKey, MondayToken = dto.MondayToken,
        MondayBoardId = dto.MondayBoardId, GitRepoPath = dto.GitRepoPath
    };

    private static DevToolsSessionDto ToSessionDto(DevToolsSession s)
    {
        var messages = new List<DevToolsChatMessage>();
        try { messages = JsonSerializer.Deserialize<List<DevToolsChatMessage>>(s.MessagesJson, _json) ?? new(); } catch { }
        return new DevToolsSessionDto
        {
            Id = s.Id, WorkspaceId = s.WorkspaceId, TaskId = s.TaskId,
            TaskSource = s.TaskSource, TaskTitle = s.TaskTitle, TaskDescription = s.TaskDescription,
            Messages = messages, SummaryContext = s.SummaryContext, TotalTokensUsed = s.TotalTokensUsed
        };
    }

    private static List<DevToolsChatMessage> DeserializeMessages(string json)
    {
        try { return JsonSerializer.Deserialize<List<DevToolsChatMessage>>(json, _json) ?? new(); }
        catch { return new(); }
    }
}
