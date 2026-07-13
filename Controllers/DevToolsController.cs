using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Models;
using SkanyxxWeb.Services;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/devtools")]
public class DevToolsController : ControllerBase
{
    private readonly IDevToolsService _svc;
    private readonly DevToolsHybridService _hybrid;

    public DevToolsController(IDevToolsService svc, DevToolsHybridService hybrid)
    {
        _svc    = svc;
        _hybrid = hybrid;
    }

    // ── Workspaces ────────────────────────────────────────────────────────────

    [HttpGet("workspaces")]
    public async Task<IActionResult> GetWorkspaces()
    {
        var result = await _svc.GetWorkspacesAsync();
        return Ok(result);
    }

    [HttpPost("workspaces")]
    public async Task<IActionResult> CreateWorkspace([FromBody] DevToolsWorkspaceDto dto)
    {
        try
        {
            var created = await _svc.CreateWorkspaceAsync(dto);
            return Ok(created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("workspaces/{id:int}")]
    public async Task<IActionResult> UpdateWorkspace(int id, [FromBody] DevToolsWorkspaceDto dto)
    {
        var result = await _svc.UpdateWorkspaceAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("workspaces/{id:int}")]
    public async Task<IActionResult> DeleteWorkspace(int id)
    {
        var deleted = await _svc.DeleteWorkspaceAsync(id);
        return deleted ? Ok() : NotFound();
    }

    // ── Admin — LLM Connections ───────────────────────────────────────────────

    [HttpGet("admin/connections")]
    public async Task<IActionResult> GetConnections() =>
        Ok(await _svc.GetLlmConnectionsAsync());

    [HttpPost("admin/connections")]
    public async Task<IActionResult> CreateConnection([FromBody] DevToolsLlmConnectionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { error = "Name is required." });
        return Ok(await _svc.CreateLlmConnectionAsync(dto));
    }

    [HttpPut("admin/connections/{id:int}")]
    public async Task<IActionResult> UpdateConnection(int id, [FromBody] DevToolsLlmConnectionDto dto)
    {
        var result = await _svc.UpdateLlmConnectionAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("admin/connections/{id:int}")]
    public async Task<IActionResult> DeleteConnection(int id)
    {
        var deleted = await _svc.DeleteLlmConnectionAsync(id);
        return deleted ? Ok() : NotFound();
    }

    [HttpPost("admin/connections/{id:int}/test")]
    public async Task<IActionResult> TestConnection(int id)
    {
        var result = await _svc.TestLlmConnectionAsync(id);
        return Ok(result);
    }

    [HttpPost("admin/settings/test")]
    public async Task<IActionResult> TestTaskSource([FromQuery] string source)
    {
        var result = await _svc.TestTaskSourceAsync(source);
        return Ok(result);
    }

    // ── Admin — Global Settings ───────────────────────────────────────────────

    [HttpGet("admin/settings")]
    public async Task<IActionResult> GetGlobalSettings() =>
        Ok(await _svc.GetGlobalSettingsAsync());

    [HttpPut("admin/settings")]
    public async Task<IActionResult> SaveGlobalSettings([FromBody] DevToolsAdminGlobalDto dto)
    {
        await _svc.SaveGlobalSettingsAsync(dto);
        return Ok();
    }

    // ── Skills ────────────────────────────────────────────────────────────────

    [HttpGet("skills")]
    public async Task<IActionResult> GetSkills()
    {
        return Ok(await _svc.GetSkillsAsync());
    }

    [HttpPost("skills")]
    public async Task<IActionResult> CreateSkill([FromBody] DevToolsSkillDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { error = "Name is required." });
        if (string.IsNullOrWhiteSpace(dto.SystemPromptAddition)) return BadRequest(new { error = "Prompt is required." });
        return Ok(await _svc.CreateSkillAsync(dto));
    }

    [HttpPut("skills/{id:int}")]
    public async Task<IActionResult> UpdateSkill(int id, [FromBody] DevToolsSkillDto dto)
    {
        var result = await _svc.UpdateSkillAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("skills/{id:int}")]
    public async Task<IActionResult> DeleteSkill(int id)
    {
        var deleted = await _svc.DeleteSkillAsync(id);
        return deleted ? Ok() : BadRequest(new { error = "Skill not found or is a built-in skill." });
    }

    // ── Native folder picker ──────────────────────────────────────────────────

    /// <summary>
    /// Opens the OS-native folder browser dialog on the server machine and returns
    /// the selected path. Works on macOS (osascript), Windows (PowerShell/WinForms),
    /// and Linux (zenity / kdialog).
    /// </summary>
    [HttpGet("browse-folder")]
    public async Task<IActionResult> BrowseFolder([FromQuery] string? initial = null)
    {
        string? path = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var def = !string.IsNullOrEmpty(initial) && Directory.Exists(initial)
                ? $" default location POSIX file \"{initial.Replace("\"", "\\\"")}\""
                : "";
            path = await RunDialogAsync("osascript",
                $"-e \"POSIX path of (choose folder with prompt \\\"Select project folder\\\"{def})\"");
            path = path?.Trim().TrimEnd('/');
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var initArg = !string.IsNullOrEmpty(initial) ? $"; $f.SelectedPath = '{initial}'" : "";
            path = await RunDialogAsync("powershell",
                $"-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms{initArg};" +
                "$f = New-Object System.Windows.Forms.FolderBrowserDialog;" +
                "if ($f.ShowDialog() -eq 'OK') {{ Write-Output $f.SelectedPath }}\"");
            path = path?.Trim();
        }
        else
        {
            // Linux: try zenity first, then kdialog
            var titleArg = !string.IsNullOrEmpty(initial) ? $"--filename=\"{initial}\"" : "";
            path = await RunDialogAsync("zenity", $"--file-selection --directory --title=\"Select project folder\" {titleArg}");
            if (string.IsNullOrEmpty(path?.Trim()))
                path = await RunDialogAsync("kdialog", $"--getexistingdirectory \"{initial ?? "/"}\"");
            path = path?.Trim();
        }

        if (string.IsNullOrEmpty(path))
            return Ok(new { path = (string?)null, cancelled = true });

        return Ok(new { path, cancelled = false });
    }

    private static async Task<string?> RunDialogAsync(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }

    // ── Local filesystem ─────────────────────────────────────────────────────

    /// <summary>Lists files in a directory (one level, non-recursive by default).</summary>
    [HttpGet("fs/ls")]
    public IActionResult FsList([FromQuery] string path, [FromQuery] bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return BadRequest(new { error = "Directory not found." });

        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bin","obj",".git","node_modules",".vs","dist","build","__pycache__",".idea" };

        IEnumerable<string> entries;
        if (recursive)
        {
            entries = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => skip.Contains(p)));
        }
        else
        {
            entries = Directory.EnumerateFileSystemEntries(path)
                .Where(e => !skip.Contains(Path.GetFileName(e)));
        }

        var items = entries.Take(500).Select(e =>
        {
            var isDir = Directory.Exists(e);
            return new { name = Path.GetFileName(e), path = e, isDir, ext = isDir ? "" : Path.GetExtension(e).ToLower() };
        }).OrderBy(i => !i.isDir).ThenBy(i => i.name);

        return Ok(items);
    }

    /// <summary>Reads a single file from the local filesystem (max 200 KB).</summary>
    [HttpGet("fs/read")]
    public IActionResult FsRead([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return BadRequest(new { error = "File not found." });

        var info = new FileInfo(path);
        if (info.Length > 200 * 1024)
            return BadRequest(new { error = $"File too large ({info.Length / 1024} KB). Max 200 KB." });

        var content = System.IO.File.ReadAllText(path);
        return Ok(new { path, name = info.Name, content, size = info.Length });
    }

    // ── Tasks ─────────────────────────────────────────────────────────────────

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks([FromQuery] int workspaceId, [FromQuery] string source = "jira")
    {
        var tasks = await _svc.GetTasksAsync(workspaceId, source);
        return Ok(tasks);
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] DevToolsChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required." });

        var result = await _svc.SendMessageAsync(request);
        if (!string.IsNullOrEmpty(result.Error))
            return StatusCode(502, new { error = result.Error });

        return Ok(result);
    }

    // ── Session ───────────────────────────────────────────────────────────────

    [HttpGet("sessions/{workspaceId:int}")]
    public async Task<IActionResult> GetSession(int workspaceId, [FromQuery] string taskId = "")
    {
        var session = await _svc.GetSessionAsync(workspaceId, taskId);
        return session is null ? Ok(new DevToolsSessionDto()) : Ok(session);
    }

    [HttpPost("sessions/{workspaceId:int}/summarize")]
    public async Task<IActionResult> SummarizeSession(int workspaceId, [FromQuery] string taskId = "")
    {
        var session = await _svc.SummarizeSessionAsync(workspaceId, taskId);
        return Ok(session);
    }

    [HttpDelete("sessions/{workspaceId:int}")]
    public async Task<IActionResult> DeleteSession(int workspaceId, [FromQuery] string taskId = "")
    {
        if (string.IsNullOrEmpty(taskId))
        {
            var count = await _svc.DeleteAllSessionsAsync(workspaceId);
            return Ok(new { deleted = count });
        }
        var deleted = await _svc.DeleteSessionAsync(workspaceId, taskId);
        return deleted ? Ok(new { deleted = 1 }) : Ok(new { deleted = 0 });
    }

    // ── Agent actions ─────────────────────────────────────────────────────────

    [HttpPost("actions")]
    public async Task<IActionResult> ExecuteAction([FromBody] DevToolsAgentActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ActionType))
            return BadRequest(new { error = "ActionType is required." });

        var result = await _svc.ExecuteAgentActionAsync(request);
        return Ok(result);
    }

    // ── Hybrid mode ───────────────────────────────────────────────────────────

    [HttpPost("hybrid/broadcast")]
    public async Task<IActionResult> HybridBroadcast([FromBody] HybridChatRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "Message is required." });
        if (req.ConnectionIds.Count == 0)
            return BadRequest(new { error = "Select at least one LLM connection." });

        var results = await _hybrid.BroadcastAsync(req);
        return Ok(results);
    }

    [HttpPost("hybrid/review")]
    public async Task<IActionResult> HybridReview([FromBody] HybridReviewRequest req)
    {
        if (req.ReviewerConnectionId == 0)
            return BadRequest(new { error = "ReviewerConnectionId is required." });

        var result = await _hybrid.ReviewAsync(req);
        return Ok(result);
    }

    [HttpPost("hybrid/fix")]
    public async Task<IActionResult> HybridFix([FromBody] HybridFixRequest req)
    {
        if (req.AuthorConnectionId == 0)
            return BadRequest(new { error = "AuthorConnectionId is required." });

        var result = await _hybrid.FixAsync(req);
        return Ok(result);
    }
}
