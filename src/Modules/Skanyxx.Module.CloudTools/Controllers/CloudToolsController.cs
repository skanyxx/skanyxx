using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.CloudTools.Controllers;

[ApiController]
[Route("api/cloud")]
public class CloudToolsController : ControllerBase
{
    private readonly ICloudProviderService _cloudService;
    private readonly IKubernetesService _kubernetesService;
    private readonly ILogger<CloudToolsController> _logger;

    public CloudToolsController(ICloudProviderService cloudService, IKubernetesService kubernetesService, ILogger<CloudToolsController> logger)
    {
        _cloudService = cloudService;
        _kubernetesService = kubernetesService;
        _logger = logger;
    }

    [HttpGet("providers")]
    public async Task<ActionResult<List<CloudProvider>>> GetProviders() => await _cloudService.GetProvidersAsync();

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
        try { await _cloudService.ConnectAsync(id, credentials); return Ok(); }
        catch (NotImplementedException ex) { return StatusCode(501, new { message = ex.Message }); }
    }

    [HttpPost("providers/{id}/disconnect")]
    public async Task<ActionResult> Disconnect(string id)
    {
        try { await _cloudService.DisconnectAsync(id); return Ok(); }
        catch (NotImplementedException ex) { return StatusCode(501, new { message = ex.Message }); }
    }

    [HttpPost("scale-up")]
    public async Task<ActionResult> ScaleUp([FromBody] ScaleRequest request)
    {
        try { await _kubernetesService.ScaleDeploymentAsync(request.Name, request.Namespace, request.Replicas); return Ok(new { message = "Scale up initiated" }); }
        catch (NotImplementedException ex) { return StatusCode(501, new { message = ex.Message }); }
    }

    [HttpPost("scale-down")]
    public async Task<ActionResult> ScaleDown([FromBody] ScaleRequest request)
    {
        try { await _kubernetesService.ScaleDeploymentAsync(request.Name, request.Namespace, request.Replicas); return Ok(new { message = "Scale down initiated" }); }
        catch (NotImplementedException ex) { return StatusCode(501, new { message = ex.Message }); }
    }
}

[ApiController]
[Route("api/cloudtools")]
public class CloudToolsApiController : ControllerBase
{
    private readonly ILogger<CloudToolsApiController> _logger;

    public CloudToolsApiController(ILogger<CloudToolsApiController> logger) { _logger = logger; }

    [HttpGet("status")]
    public ActionResult<object> GetToolStatus()
    {
        var azureInfo = CheckToolAvailability("azure-resource-finder", new[] { "azure-resource-finder", "arf" });
        var ruchyInfo = CheckToolAvailability("ruchy", new[] { "ruchy" });
        var azureAuth = CheckAzureAuthStatus();
        return new { azure = azureInfo.Available, azurePath = azureInfo.Path, ruchy = ruchyInfo.Available, ruchyPath = ruchyInfo.Path, azureAuth = new { available = azureAuth.Available, loggedIn = azureAuth.LoggedIn, user = azureAuth.User, error = azureAuth.Error } };
    }

    [HttpGet("azure-auth")]
    public ActionResult<object> GetAzureAuth()
    {
        var auth = CheckAzureAuthStatus();
        return new { available = auth.Available, loggedIn = auth.LoggedIn, user = auth.User, error = auth.Error };
    }

    [HttpGet("test-azure")]
    public ActionResult<object> TestAzureCLI()
    {
        try
        {
            var versionResult = RunCommand("az", "--version");
            var versionAvailable = versionResult.Success;
            var version = versionAvailable ? ExtractAzureVersion(versionResult.Output) : "";
            var accountResult = RunCommand("az", "account show");
            var accountAvailable = accountResult.Success;
            return new { versionAvailable, accountAvailable, version, error = !versionAvailable ? "Azure CLI not found" : (!accountAvailable ? "Not logged in" : "") };
        }
        catch (Exception ex) { return new { versionAvailable = false, accountAvailable = false, version = "", error = ex.Message }; }
    }

    [HttpGet("search")]
    public ActionResult<object> SearchResources([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new { success = false, error = "Query is required" };
        try
        {
            var auth = CheckAzureAuthStatus();
            if (!auth.Available) return new { success = false, error = "Azure CLI not found. Please install Azure CLI." };
            if (!auth.LoggedIn) return new { success = false, error = "Not logged in to Azure. Run 'az login' in your terminal." };
            var arfInfo = CheckToolAvailability("azure-resource-finder", new[] { "azure-resource-finder", "arf" });
            if (arfInfo.Available)
            {
                var result = RunCommand(arfInfo.Path!, query);
                return new { success = result.Success, output = result.Output, error = result.Stderr };
            }
            var azResult = RunCommand("az", query);
            return new { success = azResult.Success, output = azResult.Output, error = azResult.Stderr };
        }
        catch (Exception ex) { return new { success = false, error = ex.Message }; }
    }

    [HttpPost("repl")]
    public ActionResult<object> ExecuteRepl([FromBody] ReplRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Command)) return new { success = false, error = "Command is required" };
        try
        {
            var ruchyInfo = CheckToolAvailability("ruchy", new[] { "ruchy" });
            if (!ruchyInfo.Available) return new { success = false, error = "Ruchy not found. Install with: cargo install ruchy" };
            var result = RunCommand(ruchyInfo.Path!, $"-e \"{request.Command.Replace("\"", "\\\"")}\"");
            return new { success = result.Success, output = result.Output, error = result.Stderr };
        }
        catch (Exception ex) { return new { success = false, error = ex.Message }; }
    }

    private ToolInfo CheckToolAvailability(string name, string[] commands)
    {
        foreach (var cmd in commands)
        {
            var path = FindInPath(cmd);
            if (!string.IsNullOrEmpty(path)) return new ToolInfo { Available = true, Path = path };
        }
        return new ToolInfo { Available = false };
    }

    private string? FindInPath(string command)
    {
        try
        {
            var result = RunCommand("which", command);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output)) return result.Output.Trim();
        }
        catch { }
        var commonPaths = new[] { $"/usr/local/bin/{command}", $"/usr/bin/{command}", $"/opt/homebrew/bin/{command}", $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.cargo/bin/{command}" };
        foreach (var path in commonPaths) { if (System.IO.File.Exists(path)) return path; }
        return null;
    }

    private AzureAuthInfo CheckAzureAuthStatus()
    {
        try
        {
            var azPath = FindInPath("az");
            if (string.IsNullOrEmpty(azPath)) return new AzureAuthInfo { Available = false, LoggedIn = false, Error = "Azure CLI not found" };
            var result = RunCommand("az", "account show --output json");
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                var user = "";
                try { var json = System.Text.Json.JsonDocument.Parse(result.Output); if (json.RootElement.TryGetProperty("user", out var userElement) && userElement.TryGetProperty("name", out var nameElement)) user = nameElement.GetString() ?? ""; } catch { }
                return new AzureAuthInfo { Available = true, LoggedIn = true, User = user };
            }
            return new AzureAuthInfo { Available = true, LoggedIn = false, Error = "Not logged in. Run 'az login' in your terminal." };
        }
        catch (Exception ex) { return new AzureAuthInfo { Available = false, LoggedIn = false, Error = ex.Message }; }
    }

    private string ExtractAzureVersion(string output)
    {
        var lines = output.Split('\n');
        foreach (var line in lines) { if (line.StartsWith("azure-cli")) return line.Trim(); }
        return output.Split('\n').FirstOrDefault() ?? "";
    }

    private CommandResult RunCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = command, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            path = $"/usr/local/bin:/usr/bin:/opt/homebrew/bin:{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.cargo/bin:{path}";
            psi.Environment["PATH"] = path;
            using var process = Process.Start(psi);
            if (process == null) return new CommandResult { Success = false, Stderr = "Failed to start process" };
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return new CommandResult { Success = process.ExitCode == 0, Stdout = output, Stderr = error };
        }
        catch (Exception ex) { return new CommandResult { Success = false, Stderr = ex.Message }; }
    }

    private class AzureAuthInfo { public bool Available { get; set; } public bool LoggedIn { get; set; } public string? User { get; set; } public string? Error { get; set; } }
}

public class ReplRequest { public string Command { get; set; } = ""; }
