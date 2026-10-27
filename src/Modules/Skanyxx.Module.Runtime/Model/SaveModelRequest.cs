namespace Skanyxx.Module.Runtime.Model;

internal sealed record SaveModelRequest(string? Provider, string? Model, string? ApiKey, string? BaseUrl);
