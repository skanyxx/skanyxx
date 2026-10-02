using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface ICommandExecutionService
{
    Task<CommandResult> ExecuteAsync(string command, string[] args, Dictionary<string, string>? env = null, int timeoutMs = 30000);
    Task<ToolInfo> CheckToolAvailabilityAsync(string toolName);
    Task<CommandResult> ExecuteKubectlAsync(string[] args);
    Task<CommandResult> ExecuteAzAsync(string[] args);
    Task<CommandResult> ExecuteAwsAsync(string[] args);
}
