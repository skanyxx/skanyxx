using Skanyxx.Core.Platform;
using Microsoft.AspNetCore.Authorization;
using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.ToolServers.Controllers;

// D116: what changes kagent is the owner's alone.
[ApiController]
[Route("api/toolservers")]
public class ToolServersController : ControllerBase
{
    private readonly IToolServerService _toolServerService;
    private readonly KAgentApiClient _kagent;

    public ToolServersController(IToolServerService toolServerService, KAgentApiClient kagent)
    {
        _toolServerService = toolServerService;
        _kagent = kagent;
    }

    [HttpGet]
    public async Task<ActionResult<List<ToolServer>>> GetAll()
    {
        return await _toolServerService.GetAllAsync();
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpPost]
    public async Task<ActionResult<ToolServer>> Create([FromBody] CreateToolServerRequest request)
    {
        try
        {
            var server = await _kagent.CreateToolServerAsync(request);
            return Created((string?)null, server);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpDelete("{namespace}/{name}")]
    public async Task<ActionResult> Delete(string @namespace, string name)
    {
        try
        {
            await _kagent.DeleteToolServerAsync(@namespace, name);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
