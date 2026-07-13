using SkanyxxWeb.Interfaces;
using System.Text.Json;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class CloudCommandService : ICloudProviderService
{
    private readonly ICommandExecutionService _commandService;
    private readonly ILogger<CloudCommandService> _logger;

    public CloudCommandService(ICommandExecutionService commandService, ILogger<CloudCommandService> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    public async Task<List<CloudProvider>> GetProvidersAsync()
    {
        var providers = new List<CloudProvider>();

        var awsInfo = await _commandService.CheckToolAvailabilityAsync("aws");
        if (awsInfo.Available)
        {
            var r = await _commandService.ExecuteAwsAsync(new[] { "sts", "get-caller-identity", "--output", "json" });
            providers.Add(new CloudProvider { Id = "aws", Name = "AWS", Type = "AWS", Status = r.Success ? "Connected" : "NotAuthenticated" });
        }

        var azInfo = await _commandService.CheckToolAvailabilityAsync("az");
        if (azInfo.Available)
        {
            var r = await _commandService.ExecuteAzAsync(new[] { "account", "show", "--output", "json" });
            providers.Add(new CloudProvider { Id = "azure", Name = "Azure", Type = "Azure", Status = r.Success ? "Connected" : "NotAuthenticated" });
        }

        var gcloudInfo = await _commandService.CheckToolAvailabilityAsync("gcloud");
        if (gcloudInfo.Available)
        {
            var r = await _commandService.ExecuteAsync("gcloud", new[] { "auth", "list", "--format=json" });
            providers.Add(new CloudProvider { Id = "gcp", Name = "GCP", Type = "GCP", Status = r.Success ? "Connected" : "NotAuthenticated" });
        }

        return providers;
    }

    public Task<CloudProvider?> GetProviderAsync(string id)       => throw new NotImplementedException();
    public Task<bool> ConnectAsync(string id, object credentials) => throw new NotImplementedException();
    public Task<bool> DisconnectAsync(string id)                  => throw new NotImplementedException();

    public async Task<Dictionary<string, int>> GetResourceCountsAsync(string providerId)
    {
        var counts = new Dictionary<string, int>();
        if (providerId == "aws")
        {
            var r = await _commandService.ExecuteAwsAsync(new[] { "ec2", "describe-instances", "--query", "Reservations[*].Instances[*].[InstanceId]", "--output", "json" });
            if (r.Success)
            {
                try { counts["EC2 Instances"] = JsonDocument.Parse(r.Stdout).RootElement.EnumerateArray().SelectMany(x => x.EnumerateArray()).Count(); }
                catch { }
            }
        }
        return counts;
    }
}
