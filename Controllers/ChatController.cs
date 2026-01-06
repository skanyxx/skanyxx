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

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
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
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpGet("{conversationId}")]
    public async Task<ActionResult<List<ChatMessage>>> GetConversation(string conversationId)
    {
        var messages = await _chatService.GetConversationAsync(conversationId);
        if (messages.Count == 0) return NotFound();
        return messages;
    }

    [HttpDelete("{conversationId}")]
    public async Task<ActionResult> DeleteConversation(string conversationId)
    {
        await _chatService.DeleteConversationAsync(conversationId);
        return NoContent();
    }
}
