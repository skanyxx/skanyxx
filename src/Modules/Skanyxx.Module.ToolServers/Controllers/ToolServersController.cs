using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.ToolServers.Controllers;

// D116: what changes kagent is the owner's alone.
[ApiController]
[Route("api/toolservers")]
public partial class ToolServersController : ControllerBase
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

    /// <summary>kagent's create body is the RemoteMCPServer object itself: <c>{metadata: {name, namespace}, spec: {url, protocol}}</c> (D313).</summary>
    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateToolServerRequest request, CancellationToken ct)
    {
        var ns = string.IsNullOrWhiteSpace(request.Namespace) ? "kagent" : request.Namespace.Trim();
        if (request.Name is not { } name || !DnsLabel().IsMatch(name) || !DnsLabel().IsMatch(ns))
            return BadRequest(new { error = "Name and namespace must be lowercase letters, digits and hyphens (at most 63, starting and ending with a letter or digit)." });
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            return BadRequest(new { error = "The URL must be an absolute http or https address." });
        // The spec is plain text in the cluster: a credential in the URL would be readable by anyone who reads the CR.
        if (url.UserInfo.Length > 0)
            return BadRequest(new { error = "The URL must not carry a user name or password." });
        try
        {
            if ((await _kagent.GetToolServerListAsync(ct)).Any(s => s?["ref"] is JsonValue r && r.TryGetValue<string>(out var v) && v == $"{ns}/{name}"))
                return Conflict(new { error = $"A tool server named {ns}/{name} already exists." });
            await _kagent.CreateRemoteMcpServerAsync(new JsonObject
            {
                ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = ns },
                ["spec"] = new JsonObject { ["url"] = url.AbsoluteUri, ["protocol"] = "STREAMABLE_HTTP", ["description"] = "" }
            }, [], ct);
            return Created((string?)null, new { @ref = $"{ns}/{name}" });
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode is >= 400 and < 500)
        {
            return BadRequest(new { error = "kagent refused this tool server." });
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "kagent did not create the tool server. Try again in a moment." });
        }
    }

    [Authorize(Roles = SkanyxxRoles.Owner)]
    [HttpDelete("{namespace}/{name}")]
    public async Task<ActionResult> Delete(string @namespace, string name, CancellationToken ct)
    {
        try
        {
            return await _kagent.DeleteRemoteMcpServerAsync(@namespace, name, ct)
                ? NoContent()
                : NotFound(new { error = "No such tool server." });
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "kagent did not delete the tool server. Try again in a moment." });
        }
    }

    // \z, not $: in .NET, $ also matches before a trailing "\n".
    [GeneratedRegex(@"^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?\z")]
    private static partial Regex DnsLabel();
}
