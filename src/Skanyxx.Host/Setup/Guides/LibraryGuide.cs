namespace Skanyxx.Host.Setup.Guides;

/// <summary>The Library page over the memory bank. Facts: README "Memory engine" → "Library".</summary>
public static class LibraryGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "library",
        Title: "Library",
        Readme: "Memory engine → Library",
        What: "How people see the memory bank: search, filter by scope, open a card, lift it to a team, department or company, rename its key. " +
              "Every rule is memory's, so it needs only the memory module and its database.",
        Prerequisites:
        [
            "The memory module on (Modules:Enabled:memory, on by default) and its Postgres reachable.",
            "Optional: an object store for bodies larger than a card (Memory:ObjectStore).",
        ],
        Settings:
        [
            new("ConnectionStrings:Memory", "There is no card store, so the Library has nothing to show (the Host does not start).",
                "A Postgres connection string from your secret store (Helm: the database Secret, key ConnectionStrings__Memory)."),
            new("Memory:ObjectStore:Endpoint", "Large bodies are not stored apart; the \"Open the full body\" link never appears.",
                "An S3 endpoint; on the chart the bundled MinIO sets it (minio.enabled) or external.objectStore.*.", Optional: true),
        ],
        Local:
        [
            new("Start Postgres.", "docker compose up -d"),
            new("Run the Host and sign in.", "scripts/dev/first-hour.sh host"),
        ],
        Helm:
        [
            new("Nothing to set with the bundled Postgres and MinIO. Bring your own object store with minio.enabled=false and external.objectStore.endpoint, .bucket, .existingSecret (keys access-key, secret-key)."),
        ],
        Verify:
        [
            new("Open /Library: the newest company cards are listed; search for a word from one of them."),
        ],
        Errors:
        [
            new("An unpublished company card answers 404", "Only supervisors and its author open it."),
            new("Lift is refused (403)", "Lifting into company needs a supervisor; into a team or department, membership of it."),
        ]);
}
