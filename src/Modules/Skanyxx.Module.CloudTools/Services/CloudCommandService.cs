using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.CloudTools.Services;

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

        // Check AWS
        var awsInfo = await _commandService.CheckToolAvailabilityAsync("aws");
        if (awsInfo.Available)
        {
            var awsResult = await _commandService.ExecuteAwsAsync(new[] { "sts", "get-caller-identity", "--output", "json" });
            providers.Add(new CloudProvider
            {
                Id = "aws",
                Name = "AWS",
                Type = "AWS",
                Status = awsResult.Success ? "Connected" : "NotAuthenticated"
            });
        }

        // Check Azure
        var azInfo = await _commandService.CheckToolAvailabilityAsync("az");
        if (azInfo.Available)
        {
            var azResult = await _commandService.ExecuteAzAsync(new[] { "account", "show", "--output", "json" });
            providers.Add(new CloudProvider
            {
                Id = "azure",
                Name = "Azure",
                Type = "Azure",
                Status = azResult.Success ? "Connected" : "NotAuthenticated"
            });
        }

        // Check GCP
        var gcloudInfo = await _commandService.CheckToolAvailabilityAsync("gcloud");
        if (gcloudInfo.Available)
        {
            var gcpResult = await _commandService.ExecuteAsync("gcloud", new[] { "auth", "list", "--format=json" });
            providers.Add(new CloudProvider
            {
                Id = "gcp",
                Name = "GCP",
                Type = "GCP",
                Status = gcpResult.Success ? "Connected" : "NotAuthenticated"
            });
        }

        return providers;
    }

    public Task<CloudProvider?> GetProviderAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ConnectAsync(string id, object credentials)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DisconnectAsync(string id)
    {
        throw new NotImplementedException();
    }

    public async Task<Dictionary<string, int>> GetResourceCountsAsync(string providerId)
    {
        var counts = new Dictionary<string, int>();

        if (providerId == "aws")
        {
            // Get EC2 instances count
            var ec2Result = await _commandService.ExecuteAwsAsync(new[]
            {
                "ec2", "describe-instances", "--query", "Reservations[*].Instances[*].[InstanceId]", "--output", "json"
            });
            if (ec2Result.Success)
            {
                try
                {
                    var json = JsonDocument.Parse(ec2Result.Stdout);
                    counts["EC2 Instances"] = json.RootElement.EnumerateArray()
                        .SelectMany(r => r.EnumerateArray())
                        .Count();
                }
                catch { }
            }
        }

        return counts;
    }
}
