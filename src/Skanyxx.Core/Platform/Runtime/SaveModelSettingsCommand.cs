using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Runtime;

/// <summary>
/// The owner sets the model every seed agent uses (D3): kagent's ModelConfig is created or updated through kagent's API,
/// and a non-empty <paramref name="ApiKey"/> goes straight into the Kubernetes Secret kagent keeps for it — Skanyxx
/// stores and logs nothing of it. A null or empty key keeps the current one (same provider only).
/// <paramref name="BaseUrl"/> is the Ollama host, or the OpenAI/Anthropic base URL; empty means the provider's default.
/// </summary>
public sealed record SaveModelSettingsCommand(string ActorId, string Provider, string Model, string? ApiKey, string? BaseUrl)
    : IRequest<Outcome<ModelSettings>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ActorId = {ActorId}, Provider = {Provider}, Model = {Model}, ApiKey = {(string.IsNullOrEmpty(ApiKey) ? "none" : "***")}, BaseUrl = {BaseUrl}");
        return true;
    }
}
