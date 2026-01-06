using SkanyxxWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;
using SkanyxxWeb.Services;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService chatService, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] ChatRequest request)
    {
        try
        {
            return await _chatService.SendMessageAsync(request);
        }
        catch (NotImplementedException ex)
        {
            _logger.LogWarning(ex, "Feature not implemented");
            return StatusCode(501, new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "KAgent API request failed");
            // Return the actual error message from KAgent
            return StatusCode(502, new { message = $"KAgent API error: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat request failed for agent {AgentName}", request.AgentName);
            // Return detailed error message
            return StatusCode(500, new { message = $"Failed to communicate with agent: {ex.Message}" });
        }
    }

    [HttpGet("{conversationId}")]
    public async Task<ActionResult<List<ChatMessage>>> GetConversation(string conversationId)
    {
        try
        {
            var messages = await _chatService.GetConversationAsync(conversationId);
            if (messages.Count == 0) return NotFound(new { message = "Conversation not found" });
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get conversation {Id}", conversationId);
            return StatusCode(500, new { message = $"Failed to retrieve conversation: {ex.Message}" });
        }
    }

    [HttpDelete("{conversationId}")]
    public async Task<ActionResult> DeleteConversation(string conversationId)
    {
        try
        {
            await _chatService.DeleteConversationAsync(conversationId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete conversation {Id}", conversationId);
            return StatusCode(500, new { message = $"Failed to delete conversation: {ex.Message}" });
        }
    }
}
