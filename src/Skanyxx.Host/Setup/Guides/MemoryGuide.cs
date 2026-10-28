namespace Skanyxx.Host.Setup.Guides;

/// <summary>Company memory for agents over MCP. Facts: README "Memory engine", "First hour" (seed-secret), deploy/kagent/memory/README.md.</summary>
public static class MemoryGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "memory",
        Title: "Memory",
        Readme: "Memory engine",
        What: "Company knowledge as small cards in Postgres. Agents reach it over MCP at /mcp/memory with a per-agent secret; " +
              "the seed agent gets its secret from the owner once per database (seed-secret), and until then its memory calls answer 401.",
        Prerequisites:
        [
            "Postgres (local: docker compose on 127.0.0.1:55432; Helm: bundled, or your own).",
            "kagent and the seed agent applied (see the Chat guide).",
            "The owner account created on /Setup.",
        ],
        Settings:
        [
            new("ConnectionStrings:Memory", "The Host does not start: memory has no database.",
                "A Postgres connection string from your secret store (Helm: the database Secret, key ConnectionStrings__Memory)."),
            new("Memory:McpPort", "/mcp/memory is served on the UI/API port instead of a port of its own.",
                "8081 on the chart (skanyxx.service.mcpPort); leave unset for a local run.", Optional: true),
            new("Memory:ObjectStore:Endpoint", "Card bodies stay in Postgres (at most 20,000 characters).",
                "An S3 endpoint (the chart's MinIO sets it); keys Memory:ObjectStore:AccessKey/SecretKey come from a secret store.", Optional: true),
        ],
        Local:
        [
            new("Start Postgres (the schema is migrated at startup).", "docker compose up -d"),
            new("Run kagent, the seed and the Host.", "scripts/dev/first-hour.sh up && scripts/dev/first-hour.sh host"),
            new("Issue the seed's memory secret as the owner (it goes straight into the Secret the seed reads; never a file or argv). Again to rotate.",
                "OWNER_EMAIL=<owner email> OWNER_PASSWORD=<read from your password manager> scripts/dev/first-hour.sh seed-secret"),
        ],
        Helm:
        [
            new("The chart sets Memory:McpPort=8081 and admits only kagent to that port; the Ingress never routes /mcp."),
            new("Issue the seed's secret and apply the seed into the release namespace.",
                "OWNER_EMAIL=<owner email> OWNER_PASSWORD=<read from your password manager> scripts/helm/seed-agent.sh"),
            new("With your own kagent: set networkPolicy.mcpFrom to its peers and point its RemoteMCPServer at http://<release>.<namespace>.svc:8081/mcp/memory."),
        ],
        Verify:
        [
            new("/health lists the memory database Healthy.", "curl -fsS http://localhost:5287/health"),
            new("Ask the seed on /Chat about something saved on /Library: its answer uses the card."),
        ],
        Errors:
        [
            new("The seed's memory calls answer 401", "Run seed-secret (local) or seed-agent.sh (Helm); a plain rollout restart does not pick up a new secret."),
            new("The Host refuses to start: memory database unreachable", "Start Postgres; an old local migration history is reset with docker compose down -v."),
            new("/mcp/memory answers 404 on 8080", "Expected with Memory:McpPort set: it is served on the mcp port only."),
        ]);
}
