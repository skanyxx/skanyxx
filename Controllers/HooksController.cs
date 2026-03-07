using SkanyxxWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;
using SkanyxxWeb.Services;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HooksController : ControllerBase
{
    private readonly IHookService _hookService;

    public HooksController(IHookService hookService)
    {
        _hookService = hookService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Hook>>> GetAll()
    {
        return await _hookService.GetAllAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Hook>> GetById(string id)
    {
        var hook = await _hookService.GetByIdAsync(id);
        if (hook == null) return NotFound();
        return hook;
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats()
    {
        var hooks = await _hookService.GetAllAsync();
        var activeCount = hooks.Count(h => h.Status?.ActiveEvents?.Count > 0);
        return Ok(new
        {
            TotalHooks = hooks.Count,
            ActiveHooks = activeCount,
            TriggersToday = 0,
            SuccessRate = 0m,
            AvgLatencyMs = 0
        });
    }

    [HttpPost]
    public async Task<ActionResult<Hook>> Create([FromBody] Hook hook)
    {
        try
        {
            var created = await _hookService.CreateAsync(hook);
            var hookId = $"{created.Metadata.Namespace}/{created.Metadata.Name}";
            return CreatedAtAction(nameof(GetById), new { id = hookId }, created);
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(string id, [FromBody] Hook updated)
    {
        try
        {
            var success = await _hookService.UpdateAsync(id, updated);
            if (!success) return NotFound();
            return Ok();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/enable")]
    public async Task<ActionResult> Enable(string id)
    {
        try
        {
            var success = await _hookService.EnableAsync(id);
            if (!success) return NotFound();
            return Ok();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/disable")]
    public async Task<ActionResult> Disable(string id)
    {
        try
        {
            var success = await _hookService.DisableAsync(id);
            if (!success) return NotFound();
            return Ok();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/test")]
    public async Task<ActionResult> Test(string id)
    {
        try
        {
            var result = await _hookService.TestAsync(id);
            return Ok(result);
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        try
        {
            var success = await _hookService.DeleteAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }
}
