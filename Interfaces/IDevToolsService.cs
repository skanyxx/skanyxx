using SkanyxxWeb.Models;

namespace SkanyxxWeb.Interfaces;

public interface IDevToolsService
{
    Task<List<DevToolsWorkspaceDto>> GetWorkspacesAsync();
    Task<DevToolsWorkspaceDto> CreateWorkspaceAsync(DevToolsWorkspaceDto dto);
    Task<DevToolsWorkspaceDto?> UpdateWorkspaceAsync(int id, DevToolsWorkspaceDto dto);
    Task<bool> DeleteWorkspaceAsync(int id);

    Task<List<DevToolsTaskItem>> GetTasksAsync(int workspaceId, string source);

    Task<DevToolsChatResponse> SendMessageAsync(DevToolsChatRequest request);
    Task<DevToolsChatResponse> StreamMessageAsync(DevToolsChatRequest request, Func<string, Task> onChunk, Func<string, Task>? onTool = null, CancellationToken ct = default);
    Task<DevToolsSessionDto?> GetSessionAsync(int workspaceId, string taskId);
    Task<DevToolsSessionDto> SummarizeSessionAsync(int workspaceId, string taskId);
    Task<bool> DeleteSessionAsync(int workspaceId, string taskId);
    Task<int> DeleteAllSessionsAsync(int workspaceId);

    Task<DevToolsActionResult> ExecuteAgentActionAsync(DevToolsAgentActionRequest request);

    // Admin — LLM connections
    Task<List<DevToolsLlmConnectionDto>> GetLlmConnectionsAsync();
    Task<DevToolsLlmConnectionDto> CreateLlmConnectionAsync(DevToolsLlmConnectionDto dto);
    Task<DevToolsLlmConnectionDto?> UpdateLlmConnectionAsync(int id, DevToolsLlmConnectionDto dto);
    Task<bool> DeleteLlmConnectionAsync(int id);

    // Admin — global settings
    Task<DevToolsAdminGlobalDto> GetGlobalSettingsAsync();
    Task SaveGlobalSettingsAsync(DevToolsAdminGlobalDto dto);
    Task<DevToolsTestResult> TestLlmConnectionAsync(int connectionId);
    Task<DevToolsTestResult> TestTaskSourceAsync(string source);

    Task<List<DevToolsSkillDto>> GetSkillsAsync();
    Task<DevToolsSkillDto> CreateSkillAsync(DevToolsSkillDto dto);
    Task<DevToolsSkillDto?> UpdateSkillAsync(int id, DevToolsSkillDto dto);
    Task<bool> DeleteSkillAsync(int id);
}
