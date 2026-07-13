using Microsoft.AspNetCore.SignalR;
using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Hubs;

/// <summary>SignalR hub for real-time DevTools chat streaming.</summary>
public sealed class DevToolsHub : Hub
{
    private readonly IDevToolsService _svc;

    public DevToolsHub(IDevToolsService svc) => _svc = svc;

    /// <summary>
    /// Invoked by the client to send a chat message.
    /// Streams response tokens back via ReceiveToken, then ReceiveDone or ReceiveError.
    /// </summary>
    public async Task SendMessage(DevToolsChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await Clients.Caller.SendAsync("ReceiveError", request.CorrelationId, "Message is required.");
            return;
        }

        var cid = request.CorrelationId;

        try
        {
            await Clients.Caller.SendAsync("ReceiveStart", cid);

            var result = await _svc.StreamMessageAsync(
                request,
                async chunk => await Clients.Caller.SendAsync("ReceiveToken", cid, chunk, Context.ConnectionAborted),
                async label => await Clients.Caller.SendAsync("ReceiveTool",  cid, label, Context.ConnectionAborted),
                Context.ConnectionAborted);

            if (!string.IsNullOrEmpty(result.Error))
                await Clients.Caller.SendAsync("ReceiveError", cid, result.Error);
            else
                await Clients.Caller.SendAsync("ReceiveDone", cid, new
                {
                    outputTokens       = result.OutputTokens,
                    totalSessionTokens = result.TotalSessionTokens
                });
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            try { await Clients.Caller.SendAsync("ReceiveError", cid, $"Server error: {ex.Message}"); }
            catch { /* caller already gone */ }
        }
    }
}
