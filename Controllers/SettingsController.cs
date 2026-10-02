using SkanyxxWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Data;
using SkanyxxWeb.Services;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(ISettingsService settings, IConfiguration configuration, ILogger<SettingsController> logger)
    {
        _settings = settings;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<Dictionary<string, string>>> GetAll()
    {
        var settings = await _settings.GetAllAsync();
        return Ok(settings);
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<string>> Get(string key)
    {
        var value = await _settings.GetAsync(key);
        if (value == null) return NotFound();
        return Ok(value);
    }

    [HttpPut("{key}")]
    public async Task<ActionResult> Set(string key, [FromBody] string value)
    {
        await _settings.SetAsync(key, value);
        return Ok(new { message = "Setting saved" });
    }

    [HttpPut]
    public async Task<ActionResult> SetMany([FromBody] Dictionary<string, string> settings)
    {
        await _settings.SetManyAsync(settings);
        return Ok(new { message = $"Saved {settings.Count} settings" });
    }

    [HttpDelete("{key}")]
    public async Task<ActionResult> Delete(string key)
    {
        await _settings.DeleteAsync(key);
        return NoContent();
    }

    // KAgent Connections
    [HttpGet("connections")]
    public async Task<ActionResult<List<KAgentConnection>>> GetConnections()
    {
        return await _settings.GetConnectionsAsync();
    }

    [HttpGet("connections/{id}")]
    public async Task<ActionResult<KAgentConnection>> GetConnection(int id)
    {
        var connection = await _settings.GetConnectionAsync(id);
        if (connection == null) return NotFound();
        return connection;
    }

    [HttpPost("connections")]
    public async Task<ActionResult<KAgentConnection>> CreateConnection([FromBody] KAgentConnection connection)
    {
        connection.Id = 0; // Ensure new ID
        var saved = await _settings.SaveConnectionAsync(connection);
        return CreatedAtAction(nameof(GetConnection), new { id = saved.Id }, saved);
    }

    [HttpPut("connections/{id}")]
    public async Task<ActionResult<KAgentConnection>> UpdateConnection(int id, [FromBody] KAgentConnection connection)
    {
        connection.Id = id;
        var saved = await _settings.SaveConnectionAsync(connection);
        return Ok(saved);
    }

    [HttpDelete("connections/{id}")]
    public async Task<ActionResult> DeleteConnection(int id)
    {
        await _settings.DeleteConnectionAsync(id);
        return NoContent();
    }

    [HttpPost("connections/{id}/default")]
    public async Task<ActionResult> SetDefaultConnection(int id)
    {
        await _settings.SetDefaultConnectionAsync(id);
        return Ok(new { message = "Default connection updated" });
    }

    // Layouts
    [HttpGet("layouts")]
    public async Task<ActionResult<List<SavedLayout>>> GetLayouts()
    {
        return await _settings.GetLayoutsAsync();
    }

    [HttpGet("layouts/{id}")]
    public async Task<ActionResult<SavedLayout>> GetLayout(int id)
    {
        var layout = await _settings.GetLayoutAsync(id);
        if (layout == null) return NotFound();
        return layout;
    }

    [HttpPost("layouts")]
    public async Task<ActionResult<SavedLayout>> CreateLayout([FromBody] SavedLayout layout)
    {
        layout.Id = 0;
        var saved = await _settings.SaveLayoutAsync(layout);
        return CreatedAtAction(nameof(GetLayout), new { id = saved.Id }, saved);
    }

    [HttpPut("layouts/{id}")]
    public async Task<ActionResult<SavedLayout>> UpdateLayout(int id, [FromBody] SavedLayout layout)
    {
        layout.Id = id;
        var saved = await _settings.SaveLayoutAsync(layout);
        return Ok(saved);
    }

    [HttpDelete("layouts/{id}")]
    public async Task<ActionResult> DeleteLayout(int id)
    {
        await _settings.DeleteLayoutAsync(id);
        return NoContent();
    }

    [HttpGet("system-info")]
    public ActionResult GetSystemInfo()
    {
        return Ok(new
        {
            Platform = "ASP.NET Core 9.0",
            Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
            MemoryUsage = $"{GC.GetTotalMemory(false) / 1024 / 1024} MB",
            Version = "1.0.0",
            Database = "SQLite"
        });
    }
}
