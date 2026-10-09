namespace Skanyxx.Module.Settings.Model;

internal sealed record SaveModelRequest(string? Provider, string? Model, string? ApiKey, string? BaseUrl);
