using SkanyxxWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;
using SkanyxxWeb.Services;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/cloud")]
public class CloudToolsController : ControllerBase
{
    private readonly ICloudProviderService _cloudService;
    private readonly IKubernetesService _kubernetesService;

    public CloudToolsController(ICloudProviderService cloudService, IKubernetesService kubernetesService)
    {
        _cloudService = cloudService;
        _kubernetesService = kubernetesService;
    }

    [HttpGet("providers")]
    public async Task<ActionResult<List<CloudProvider>>> GetProviders()
    {
        return await _cloudService.GetProvidersAsync();
    }

    [HttpGet("providers/{id}")]
    public async Task<ActionResult<CloudProvider>> GetProvider(string id)
    {
        var provider = await _cloudService.GetProviderAsync(id);
        if (provider == null) return NotFound();
        return provider;
    }

    [HttpPost("providers/{id}/connect")]
    public async Task<ActionResult> Connect(string id, [FromBody] object credentials)
    {
        try
        {
            await _cloudService.ConnectAsync(id, credentials);
            return Ok();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("providers/{id}/disconnect")]
    public async Task<ActionResult> Disconnect(string id)
    {
        try
        {
            await _cloudService.DisconnectAsync(id);
            return Ok();
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("scale-up")]
    public async Task<ActionResult> ScaleUp([FromBody] ScaleRequest request)
    {
        try
        {
            await _kubernetesService.ScaleDeploymentAsync(request.Name, request.Namespace, request.Replicas);
            return Ok(new { message = "Scale up initiated" });
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    [HttpPost("scale-down")]
    public async Task<ActionResult> ScaleDown([FromBody] ScaleRequest request)
    {
        try
        {
            await _kubernetesService.ScaleDeploymentAsync(request.Name, request.Namespace, request.Replicas);
            return Ok(new { message = "Scale down initiated" });
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }
}
