namespace Skanyxx.Core.Platform.Runtime;

/// <summary>The kagent providers the model step offers (kagent 0.10 <c>ModelConfig.spec.provider</c> values).</summary>
public static class ModelProviders
{
    public const string Ollama = "Ollama";
    public const string OpenAI = "OpenAI";
    public const string Anthropic = "Anthropic";

    public static readonly IReadOnlyList<string> All = [OpenAI, Anthropic, Ollama];
}
