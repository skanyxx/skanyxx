using SkanyxxWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;
using SkanyxxWeb.Services;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MemoryController : ControllerBase
{
    private readonly IMemoryService _memoryService;

    public MemoryController(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    [HttpGet]
    public async Task<ActionResult<List<MemoryItem>>> GetAll([FromQuery] string? category = null)
    {
        return await _memoryService.GetAllAsync(category);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MemoryItem>> GetById(string id)
    {
        var memory = await _memoryService.GetByIdAsync(id);
        if (memory == null) return NotFound();
        return memory;
    }

    [HttpGet("categories")]
    public ActionResult GetCategories()
    {
        // TODO: Get from service
        return Ok(new object[] { });
    }

    [HttpGet("stats")]
    public ActionResult GetStats()
    {
        // TODO: Get from service
        return Ok(new { TotalMemories = 0, ActiveContexts = 0, StorageUsedGb = 0, RetrievalsToday = 0 });
    }

    [HttpPost]
    public async Task<ActionResult<MemoryItem>> Create([FromBody] CreateMemoryRequest request)
    {
        try
        {
            var memory = await _memoryService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = memory.Id }, memory);
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
            var success = await _memoryService.DeleteAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<MemoryItem>>> Search([FromBody] string query)
    {
        return await _memoryService.SearchAsync(query);
    }
}
