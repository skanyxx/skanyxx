using Microsoft.EntityFrameworkCore;
using SkanyxxWeb.Data;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

/// <summary>Persists and retrieves global settings and LLM connection records.</summary>
internal sealed class DevToolsSettingsRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly DevToolsLlmClient _llm;
    private readonly DevToolsTaskFetcher _fetcher;

    // Maps DTO property name → AppSettings key
    private static readonly Dictionary<string, string> SettingKeys = new()
    {
        [nameof(DevToolsAdminGlobalDto.JiraUrl)]                = "devtools.jira.url",
        [nameof(DevToolsAdminGlobalDto.JiraEmail)]              = "devtools.jira.email",
        [nameof(DevToolsAdminGlobalDto.JiraToken)]              = "devtools.jira.token",
        [nameof(DevToolsAdminGlobalDto.JiraProjectKey)]         = "devtools.jira.project",
        [nameof(DevToolsAdminGlobalDto.MondayToken)]            = "devtools.monday.token",
        [nameof(DevToolsAdminGlobalDto.MondayBoardId)]          = "devtools.monday.boardId",
        [nameof(DevToolsAdminGlobalDto.LinearToken)]            = "devtools.linear.token",
        [nameof(DevToolsAdminGlobalDto.LinearTeamKey)]          = "devtools.linear.team",
        [nameof(DevToolsAdminGlobalDto.GitHubToken)]            = "devtools.github.token",
        [nameof(DevToolsAdminGlobalDto.GitHubOwner)]            = "devtools.github.owner",
        [nameof(DevToolsAdminGlobalDto.GitHubRepo)]             = "devtools.github.repo",
        [nameof(DevToolsAdminGlobalDto.GitLabUrl)]              = "devtools.gitlab.url",
        [nameof(DevToolsAdminGlobalDto.GitLabToken)]            = "devtools.gitlab.token",
        [nameof(DevToolsAdminGlobalDto.GitLabProjectId)]        = "devtools.gitlab.project",
        [nameof(DevToolsAdminGlobalDto.AzureDevOpsOrg)]         = "devtools.azdo.org",
        [nameof(DevToolsAdminGlobalDto.AzureDevOpsProject)]     = "devtools.azdo.project",
        [nameof(DevToolsAdminGlobalDto.AzureDevOpsPat)]         = "devtools.azdo.pat",
        [nameof(DevToolsAdminGlobalDto.AsanaToken)]             = "devtools.asana.token",
        [nameof(DevToolsAdminGlobalDto.AsanaProjectId)]         = "devtools.asana.project",
        [nameof(DevToolsAdminGlobalDto.ClickUpToken)]           = "devtools.clickup.token",
        [nameof(DevToolsAdminGlobalDto.ClickUpListId)]          = "devtools.clickup.list",
        [nameof(DevToolsAdminGlobalDto.YouTrackUrl)]            = "devtools.youtrack.url",
        [nameof(DevToolsAdminGlobalDto.YouTrackToken)]          = "devtools.youtrack.token",
        [nameof(DevToolsAdminGlobalDto.YouTrackProject)]        = "devtools.youtrack.project",
        [nameof(DevToolsAdminGlobalDto.TrelloKey)]              = "devtools.trello.key",
        [nameof(DevToolsAdminGlobalDto.TrelloToken)]            = "devtools.trello.token",
        [nameof(DevToolsAdminGlobalDto.TrelloBoardId)]          = "devtools.trello.board",
        [nameof(DevToolsAdminGlobalDto.ShortcutToken)]          = "devtools.shortcut.token",
        [nameof(DevToolsAdminGlobalDto.ShortcutWorkflowStateId)]= "devtools.shortcut.workflow",
        [nameof(DevToolsAdminGlobalDto.NotionToken)]            = "devtools.notion.token",
        [nameof(DevToolsAdminGlobalDto.NotionDatabaseId)]       = "devtools.notion.database",
    };

    private static readonly HashSet<string> SecretKeys =
    [
        "devtools.jira.token","devtools.monday.token","devtools.linear.token","devtools.github.token",
        "devtools.gitlab.token","devtools.azdo.pat","devtools.asana.token","devtools.clickup.token",
        "devtools.youtrack.token","devtools.trello.token","devtools.trello.key","devtools.shortcut.token",
        "devtools.notion.token"
    ];

    public DevToolsSettingsRepository(
        IDbContextFactory<AppDbContext> dbFactory,
        DevToolsLlmClient llm,
        DevToolsTaskFetcher fetcher)
    {
        _dbFactory = dbFactory;
        _llm       = llm;
        _fetcher   = fetcher;
    }

    // ── Global settings ───────────────────────────────────────────────────────

    public async Task<DevToolsAdminGlobalDto> LoadGlobalSettings(AppDbContext db)
    {
        var allKeys  = SettingKeys.Values.ToList();
        var settings = await db.Settings.Where(s => allKeys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value);
        string Get(string prop)  => settings.TryGetValue(SettingKeys[prop], out var v) ? v : "";
        string Mask(string prop) { var k = SettingKeys[prop]; return SecretKeys.Contains(k) && settings.ContainsKey(k) && !string.IsNullOrEmpty(settings[k]) ? "••••••••" : ""; }

        return new DevToolsAdminGlobalDto
        {
            JiraUrl=Get("JiraUrl"), JiraEmail=Get("JiraEmail"), JiraToken=Mask("JiraToken"), JiraProjectKey=Get("JiraProjectKey"),
            MondayToken=Mask("MondayToken"), MondayBoardId=Get("MondayBoardId"),
            LinearToken=Mask("LinearToken"), LinearTeamKey=Get("LinearTeamKey"),
            GitHubToken=Mask("GitHubToken"), GitHubOwner=Get("GitHubOwner"), GitHubRepo=Get("GitHubRepo"),
            GitLabUrl=Get("GitLabUrl"), GitLabToken=Mask("GitLabToken"), GitLabProjectId=Get("GitLabProjectId"),
            AzureDevOpsOrg=Get("AzureDevOpsOrg"), AzureDevOpsProject=Get("AzureDevOpsProject"), AzureDevOpsPat=Mask("AzureDevOpsPat"),
            AsanaToken=Mask("AsanaToken"), AsanaProjectId=Get("AsanaProjectId"),
            ClickUpToken=Mask("ClickUpToken"), ClickUpListId=Get("ClickUpListId"),
            YouTrackUrl=Get("YouTrackUrl"), YouTrackToken=Mask("YouTrackToken"), YouTrackProject=Get("YouTrackProject"),
            TrelloKey=Mask("TrelloKey"), TrelloToken=Mask("TrelloToken"), TrelloBoardId=Get("TrelloBoardId"),
            ShortcutToken=Mask("ShortcutToken"), ShortcutWorkflowStateId=Get("ShortcutWorkflowStateId"),
            NotionToken=Mask("NotionToken"), NotionDatabaseId=Get("NotionDatabaseId")
        };
    }

    public async Task<DevToolsAdminGlobalDto> GetGlobalSettingsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await LoadGlobalSettings(db);
    }

    public async Task SaveGlobalSettingsAsync(DevToolsAdminGlobalDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var prop in typeof(DevToolsAdminGlobalDto).GetProperties())
        {
            if (!SettingKeys.TryGetValue(prop.Name, out var dbKey)) continue;
            var value = prop.GetValue(dto) as string;
            if (value is null || value == "••••••••") continue;
            var existing = await db.Settings.FindAsync(dbKey);
            if (existing is null) db.Settings.Add(new AppSetting { Key = dbKey, Value = value });
            else existing.Value = value;
        }
        await db.SaveChangesAsync();
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    public async Task<DevToolsTestResult> TestLlmConnectionAsync(int connectionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var conn = await db.DevToolsLlmConnections.FindAsync(connectionId);
        if (conn is null) return new DevToolsTestResult { Message = "Connection not found." };
        var ws      = new DevToolsWorkspace { LlmProvider = conn.Provider, LlmModel = conn.Model, LlmUrl = conn.BaseUrl, ApiKey = conn.ApiKey };
        var testMsg = new List<DevToolsChatMessage> { new() { Role = "user", Content = "Reply with only: OK" } };
        var (reply, _, _) = await _llm.CallAsync(ws, "You are a test bot.", testMsg);
        var ok = !reply.StartsWith("Error") && !reply.StartsWith("Anthropic error") && !string.IsNullOrEmpty(reply);
        return new DevToolsTestResult { Success = ok, Message = ok ? $"OK — reply: {reply[..Math.Min(80, reply.Length)]}" : reply };
    }

    public async Task<DevToolsTestResult> TestTaskSourceAsync(string source)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var g = await LoadGlobalSettings(db);
        // Load real (unmasked) secrets for the test call
        var allKeys  = SettingKeys.Values.ToList();
        var settings = await db.Settings.Where(s => allKeys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value);
        string Real(string prop) => settings.TryGetValue(SettingKeys[prop], out var v) ? v : "";
        g.JiraToken = Real("JiraToken"); g.MondayToken = Real("MondayToken"); g.LinearToken = Real("LinearToken");
        g.GitHubToken = Real("GitHubToken"); g.GitLabToken = Real("GitLabToken"); g.AzureDevOpsPat = Real("AzureDevOpsPat");
        g.AsanaToken = Real("AsanaToken"); g.ClickUpToken = Real("ClickUpToken"); g.YouTrackToken = Real("YouTrackToken");
        g.TrelloKey = Real("TrelloKey"); g.TrelloToken = Real("TrelloToken"); g.ShortcutToken = Real("ShortcutToken");
        g.NotionToken = Real("NotionToken");
        try
        {
            var items = await _fetcher.FetchAsync(source, new DevToolsWorkspace(), g);
            return new DevToolsTestResult { Success = true, Message = $"Connected — {items.Count} open item(s) found." };
        }
        catch (Exception ex) { return new DevToolsTestResult { Success = false, Message = ex.Message }; }
    }

    // ── LLM connections ───────────────────────────────────────────────────────

    public async Task<List<DevToolsLlmConnectionDto>> GetLlmConnectionsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.DevToolsLlmConnections
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.Name)
            .Select(c => new DevToolsLlmConnectionDto
            {
                Id = c.Id, Name = c.Name, Provider = c.Provider, BaseUrl = c.BaseUrl, Model = c.Model,
                ApiKey = string.IsNullOrEmpty(c.ApiKey) ? null : "••••••••",
                IsDefault = c.IsDefault, IsEnabled = c.IsEnabled
            })
            .ToListAsync();
    }

    public async Task<DevToolsLlmConnectionDto> CreateLlmConnectionAsync(DevToolsLlmConnectionDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (dto.IsDefault)
            await db.DevToolsLlmConnections.Where(c => c.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false));
        var entity = new DevToolsLlmConnection
        {
            Name = dto.Name, Provider = dto.Provider, BaseUrl = dto.BaseUrl ?? "",
            Model = dto.Model, ApiKey = dto.ApiKey, IsDefault = dto.IsDefault,
            IsEnabled = dto.IsEnabled, CreatedAt = DateTime.UtcNow
        };
        db.DevToolsLlmConnections.Add(entity);
        await db.SaveChangesAsync();
        dto.Id = entity.Id;
        dto.ApiKey = string.IsNullOrEmpty(entity.ApiKey) ? null : "••••••••";
        return dto;
    }

    public async Task<DevToolsLlmConnectionDto?> UpdateLlmConnectionAsync(int id, DevToolsLlmConnectionDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.DevToolsLlmConnections.FindAsync(id);
        if (entity is null) return null;
        if (dto.IsDefault && !entity.IsDefault)
            await db.DevToolsLlmConnections.Where(c => c.IsDefault && c.Id != id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false));
        entity.Name = dto.Name; entity.Provider = dto.Provider;
        entity.BaseUrl = dto.BaseUrl ?? ""; entity.Model = dto.Model;
        entity.IsDefault = dto.IsDefault; entity.IsEnabled = dto.IsEnabled;
        if (!string.IsNullOrEmpty(dto.ApiKey) && dto.ApiKey != "••••••••") entity.ApiKey = dto.ApiKey;
        await db.SaveChangesAsync();
        dto.ApiKey = string.IsNullOrEmpty(entity.ApiKey) ? null : "••••••••";
        return dto;
    }

    public async Task<bool> DeleteLlmConnectionAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.DevToolsLlmConnections.FindAsync(id);
        if (entity is null) return false;
        db.DevToolsLlmConnections.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }
}
