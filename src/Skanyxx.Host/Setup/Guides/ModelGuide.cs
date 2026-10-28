namespace Skanyxx.Host.Setup.Guides;

/// <summary>The owner's model step. Facts: README "First hour" (Model).</summary>
public static class ModelGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "model",
        Title: "Model",
        Readme: "First hour (local three-plane run) → Model",
        What: "Chooses the model the seed agent answers with. The owner saves provider, model and key on /Model; Skanyxx writes them " +
              "into kagent's ModelConfig (KAgent:ModelConfig) through kagent's API, and kagent keeps the key in a Secret it owns. Skanyxx never stores or shows it.",
        Prerequisites:
        [
            "kagent running and reachable from the Host (see the Chat guide).",
            "A model: OpenAI or Anthropic (an API key from the provider's console), or Ollama serving a model the agent pods can reach (e.g. ollama pull qwen3-coder:30b).",
        ],
        Settings:
        [
            new("KAgent:BaseUrl", "Skanyxx cannot reach kagent, so the model cannot be read or saved.",
                "kagent's controller: localhost with KAgent:Port 8083 through a port-forward locally; the chart sets KAgent:IngressUrl.",
                Alternative: "KAgent:IngressUrl"),
            new("KAgent:ModelConfig", "The default ModelConfig kagent/default-model-config is used.",
                "namespace/name of the ModelConfig to edit; the chart sets <namespace>/default-model-config.", Optional: true),
        ],
        Local:
        [
            new("Start Postgres and kagent with its port-forward on :8083.", "scripts/dev/first-hour.sh up"),
            new("For Ollama, pull the model on this machine.", "ollama pull qwen3-coder:30b"),
            new("Run the Host and sign in as the owner.", "scripts/dev/first-hour.sh host"),
            new("Open /Model, choose the provider, the model and (OpenAI/Anthropic) paste the key from the provider's console; for Ollama give its host as the pods see it (http://host.docker.internal:11434). Save."),
        ],
        Helm:
        [
            new("The chart renders no default-model-config on purpose (kagent.providers=false): the model stays the owner's."),
            new("Port-forward the UI (or use the ingress), sign in as the owner and save the model on /Model.",
                "kubectl -n <namespace> port-forward svc/skanyxx 8080:8080"),
            new("A local model such as Ollama inside the cluster network also needs an egress rule for the agent pods in networkPolicy.modelEgress.extra."),
        ],
        Verify:
        [
            new("/Model says \"kagent accepted it\" and shows \"Continue to Chat\"."),
            new("Ask the seed agent a question on /Chat and get an answer."),
        ],
        Errors:
        [
            new("409 when saving without a key", "A new base URL or a new provider needs the key pasted again: a stored key is never sent to a new host."),
            new("\"kagent has not accepted it\"", "Check the model name and key with the provider; for Ollama check the host is reachable from the agent pods."),
            new("/ keeps sending the owner to /Model", "kagent has not accepted a model with its credentials yet; save it again."),
        ]);
}
