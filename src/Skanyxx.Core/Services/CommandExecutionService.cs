using Skanyxx.Core.Interfaces;
using System.Diagnostics;
using System.Text;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.Services;

public class CommandExecutionService : ICommandExecutionService
{
    private readonly ILogger<CommandExecutionService> _logger;
    private readonly IConfiguration _configuration;

    public CommandExecutionService(ILogger<CommandExecutionService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<CommandResult> ExecuteAsync(string command, string[] args, Dictionary<string, string>? env = null, int timeoutMs = 30000)
    {
        var result = new CommandResult();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            // Add environment variables
            if (env != null)
            {
                foreach (var kvp in env)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            // Add PATH for common tool locations
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var additionalPaths = new[]
            {
                "/usr/local/bin",
                "/opt/homebrew/bin",
                "/usr/bin",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/bin"
            };
            startInfo.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator, additionalPaths.Concat(new[] { path }));

            using var process = new Process { StartInfo = startInfo };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeoutMs));

            if (!completed)
            {
                process.Kill(true);
                result.Success = false;
                result.Stderr = "Command timed out";
                result.ExitCode = -1;
                return result;
            }

            result.Success = process.ExitCode == 0;
            result.Stdout = stdout.ToString().Trim();
            result.Stderr = stderr.ToString().Trim();
            result.ExitCode = process.ExitCode;

            _logger.LogDebug("Executed {Command} {Args}: ExitCode={ExitCode}", command, string.Join(" ", args), result.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command: {Command}", command);
            result.Success = false;
            result.Stderr = ex.Message;
            result.ExitCode = -1;
        }

        return result;
    }

    public async Task<ToolInfo> CheckToolAvailabilityAsync(string toolName)
    {
        var info = new ToolInfo { Name = toolName };

        // Find tool in PATH
        var whichCommand = OperatingSystem.IsWindows() ? "where" : "which";
        var result = await ExecuteAsync(whichCommand, new[] { toolName });

        if (result.Success && !string.IsNullOrEmpty(result.Stdout))
        {
            info.Available = true;
            info.Path = result.Stdout.Split('\n').FirstOrDefault()?.Trim();

            // Get version
            var versionResult = await ExecuteAsync(toolName, new[] { "--version" });
            if (versionResult.Success)
            {
                info.Version = versionResult.Stdout.Split('\n').FirstOrDefault()?.Trim();
            }
        }

        return info;
    }

    public async Task<CommandResult> ExecuteKubectlAsync(string[] args)
    {
        var kubeconfig = _configuration["Kubernetes:ConfigPath"];
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(kubeconfig))
        {
            env["KUBECONFIG"] = kubeconfig;
        }

        return await ExecuteAsync("kubectl", args, env);
    }

    public async Task<CommandResult> ExecuteAzAsync(string[] args)
    {
        var azConfigDir = _configuration["Azure:ConfigDir"];
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(azConfigDir))
        {
            env["AZURE_CONFIG_DIR"] = azConfigDir;
        }

        return await ExecuteAsync("az", args, env);
    }

    public async Task<CommandResult> ExecuteAwsAsync(string[] args)
    {
        var awsProfile = _configuration["AWS:Profile"];
        var awsRegion = _configuration["AWS:Region"];
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(awsProfile))
        {
            env["AWS_PROFILE"] = awsProfile;
        }
        if (!string.IsNullOrEmpty(awsRegion))
        {
            env["AWS_DEFAULT_REGION"] = awsRegion;
        }

        return await ExecuteAsync("aws", args, env);
    }
}
