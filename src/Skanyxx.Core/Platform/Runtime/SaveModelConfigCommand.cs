using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Runtime;

/// <summary>
/// The owner creates (<paramref name="Create"/>) or edits the kagent ModelConfig <c>Namespace/Name</c> through kagent's API.
/// <paramref name="Params"/> are the provider's fields by their kagent name (empty = unset). A non-empty
/// <paramref name="ApiKey"/> goes straight into the Secret kagent keeps for the ModelConfig — Skanyxx stores, returns and
/// logs nothing of it; null or empty keeps the stored key (same provider only). <paramref name="NoApiKey"/>: the provider
/// needs no key here (a gateway adds it), and a stored key reference is dropped.
/// </summary>
public sealed record SaveModelConfigCommand(
    string ActorId, string Namespace, string Name, bool Create, string Provider, string Model,
    IReadOnlyDictionary<string, string> Params, string? ApiKey, bool NoApiKey) : IRequest<Outcome<ModelConfigDetail>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ActorId = {ActorId}, Namespace = {Namespace}, Name = {Name}, Create = {Create}, Provider = {Provider}, Model = {Model}, ");
        builder.Append($"Params = [{string.Join(", ", Params.Keys)}], ApiKey = {(string.IsNullOrEmpty(ApiKey) ? "none" : "***")}, NoApiKey = {NoApiKey}");
        return true;
    }
}
