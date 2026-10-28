namespace Skanyxx.Module.Runtime.Models.Endpoints;

internal sealed class DeleteModelRequest
{
    public string Namespace { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary><c>?confirm=true</c>: delete although agents use the model (they were listed by the first answer).</summary>
    public bool Confirm { get; set; }
}
