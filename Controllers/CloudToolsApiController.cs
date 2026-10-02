using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;
using System.Diagnostics;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/cloudtools")]
public class CloudToolsApiController : ControllerBase
{
    private readonly ILogger<CloudToolsApiController> _logger;

    public CloudToolsApiController(ILogger<CloudToolsApiController> logger) => _logger = logger;

    [HttpGet("status")]
    public ActionResult<object> GetToolStatus()
    {
        var azureInfo = CheckToolAvailability("azure-resource-finder", new[] { "azure-resource-finder", "arf" });
        var ruchyInfo = CheckToolAvailability("ruchy", new[] { "ruchy" });
        var azureAuth = CheckAzureAuthStatus();
        return new { azure = azureInfo.Available, azurePath = azureInfo.Path, ruchy = ruchyInfo.Available, ruchyPath = ruchyInfo.Path,
            azureAuth = new { available = azureAuth.Available, loggedIn = azureAuth.LoggedIn, user = azureAuth.User, error = azureAuth.Error } };
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
            var v = RunCommand("az", "--version");
            var a = RunCommand("az", "account show");
            return new { versionAvailable = v.Success, accountAvailable = a.Success, version = v.Success ? ExtractAzureVersion(v.Output) : "",
                error = !v.Success ? "Azure CLI not found" : (!a.Success ? "Not logged in" : "") };
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
            if (!auth.LoggedIn)  return new { success = false, error = "Not logged in to Azure. Run 'az login' in your terminal." };
            var arfInfo = CheckToolAvailability("azure-resource-finder", new[] { "azure-resource-finder", "arf" });
            if (arfInfo.Available)
            {
                var r = RunCommand(arfInfo.Path!, query);
                return new { success = r.Success, output = r.Output, error = r.Stderr };
            }
            var az = RunCommand("az", query);
            return new { success = az.Success, output = az.Output, error = az.Stderr };
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
            var r = RunCommand(ruchyInfo.Path!, $"-e \"{request.Command.Replace("\"", "\\\"")}\"");
            return new { success = r.Success, output = r.Output, error = r.Stderr };
        }
        catch (Exception ex) { return new { success = false, error = ex.Message }; }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private Models.ToolInfo CheckToolAvailability(string name, string[] commands)
    {
        foreach (var cmd in commands)
        {
            var path = FindInPath(cmd);
            if (!string.IsNullOrEmpty(path)) return new Models.ToolInfo { Available = true, Path = path };
        }
        return new Models.ToolInfo { Available = false };
    }

    private string? FindInPath(string command)
    {
        try { var r = RunCommand("which", command); if (r.Success && !string.IsNullOrWhiteSpace(r.Output)) return r.Output.Trim(); } catch { }
        var commonPaths = new[] { $"/usr/local/bin/{command}", $"/usr/bin/{command}", $"/opt/homebrew/bin/{command}", $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.cargo/bin/{command}" };
        return commonPaths.FirstOrDefault(System.IO.File.Exists);
    }

    private AzureAuthInfo CheckAzureAuthStatus()
    {
        try
        {
            var azPath = FindInPath("az");
            if (string.IsNullOrEmpty(azPath)) return new AzureAuthInfo { Available = false, Error = "Azure CLI not found" };
            var result = RunCommand("az", "account show --output json");
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                var user = "";
                try { var json = System.Text.Json.JsonDocument.Parse(result.Output); if (json.RootElement.TryGetProperty("user", out var u) && u.TryGetProperty("name", out var n)) user = n.GetString() ?? ""; } catch { }
                return new AzureAuthInfo { Available = true, LoggedIn = true, User = user };
            }
            return new AzureAuthInfo { Available = true, LoggedIn = false, Error = "Not logged in. Run 'az login' in your terminal." };
        }
        catch (Exception ex) { return new AzureAuthInfo { Available = false, Error = ex.Message }; }
    }

    private string ExtractAzureVersion(string output) =>
        output.Split('\n').FirstOrDefault(l => l.StartsWith("azure-cli"))?.Trim() ?? output.Split('\n').FirstOrDefault() ?? "";

    private Models.CommandResult RunCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = command, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = $"/usr/local/bin:/usr/bin:/opt/homebrew/bin:{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.cargo/bin:{path}";
            using var process = Process.Start(psi);
            if (process is null) return new Models.CommandResult { Success = false, Stderr = "Failed to start process" };
            var output = process.StandardOutput.ReadToEnd();
            var error  = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return new Models.CommandResult { Success = process.ExitCode == 0, Stdout = output, Stderr = error };
        }
        catch (Exception ex) { return new Models.CommandResult { Success = false, Stderr = ex.Message }; }
    }

    private class AzureAuthInfo { public bool Available { get; set; } public bool LoggedIn { get; set; } public string? User { get; set; } public string? Error { get; set; } }
}

public class ReplRequest { public string Command { get; set; } = ""; }
