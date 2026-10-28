namespace Skanyxx.Module.Runtime.Models.Endpoints;

/// <summary>The form as the page sends it. <see cref="ApiKey"/> is write-only: no response ever carries it.</summary>
internal sealed class ModelForm
{
    public string? Namespace { get; set; }

    public string? Name { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public Dictionary<string, string?>? Params { get; set; }

    public string? ApiKey { get; set; }

    public bool NoApiKey { get; set; }
}
