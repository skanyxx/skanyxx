namespace Skanyxx.Module.Runtime.Models.Definition;

/// <summary>
/// The provider blocks of kagent 0.10.2's <c>ModelConfig</c> (v1alpha2), field by field with their CRD types. kagent's
/// provider list says which providers exist and which fields it requires but not their types, and it lists the Vertex
/// blocks' inline fields under an empty name; this table is what the form offers and how its values are typed.
/// </summary>
internal static class ProviderCatalog
{
    public const string Ollama = "Ollama";

    private static readonly ParamField[] Vertex =
        [Text("projectID"), Text("location"), Text("temperature"), Text("topP"), Text("topK")];

    public static readonly IReadOnlyDictionary<string, ProviderSchema> All = new Dictionary<string, ProviderSchema>
    {
        ["OpenAI"] = new("openAI",
            [Text("baseUrl"), Text("organization"), Text("temperature"), Integer("maxTokens"), Integer("maxCompletionTokens"), Text("topP"),
                Text("frequencyPenalty"), Text("presencePenalty"), Integer("seed"), Integer("n"), Integer("timeout"), Text("reasoningEffort"), Text("apiFormat")],
            ["baseUrl"], []),
        ["Anthropic"] = new("anthropic",
            [Text("baseUrl"), Integer("maxTokens"), Text("temperature"), Text("topP"), Integer("topK")], ["baseUrl"], []),
        ["AzureOpenAI"] = new("azureOpenAI",
            [Text("azureEndpoint"), Text("apiVersion"), Text("azureDeployment"), Text("temperature"), Integer("maxTokens"), Text("topP")],
            ["azureEndpoint"], ["azureEndpoint", "apiVersion"]),
        // Without a host kagent falls back to the agent pod's own localhost, which never serves Ollama (D106).
        [Ollama] = new("ollama", [Text("host")], ["host"], ["host"], TakesKey: false),
        ["Gemini"] = new("gemini", [Integer("maxOutputTokens")], [], []),
        ["GeminiVertexAI"] = new("geminiVertexAI",
            [.. Vertex, Integer("maxOutputTokens"), Integer("candidateCount"), Text("responseMimeType")], [], ["projectID", "location"]),
        ["AnthropicVertexAI"] = new("anthropicVertexAI", [.. Vertex, Integer("maxTokens")], [], ["projectID", "location"]),
        ["Bedrock"] = new("bedrock",
            [Text("region"), Boolean("promptCaching"), Text("cacheTTL"), Integer("readTimeout"), Integer("connectTimeout")], [], ["region"]),
        ["SAPAICore"] = new("sapAICore", [Text("baseUrl"), Text("resourceGroup"), Text("authUrl")], ["baseUrl", "authUrl"], ["baseUrl"]),
        ["Foundry"] = new("foundry", [Text("endpoint"), Text("deployment"), Text("apiVersion")], ["endpoint"], ["endpoint", "deployment"])
    };

    private static ParamField Text(string key) => new(key, ParamKinds.Text);

    private static ParamField Integer(string key) => new(key, ParamKinds.Integer);

    private static ParamField Boolean(string key) => new(key, ParamKinds.Boolean);
}
