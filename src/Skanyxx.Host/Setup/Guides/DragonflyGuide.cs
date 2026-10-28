namespace Skanyxx.Host.Setup.Guides;

/// <summary>The optional Dragonfly/Redis cache. Facts: README "Install with Helm" (Dragonfly), DragonflyOptions (D068), AnswerCacheOptions (D265).</summary>
public static class DragonflyGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "dragonfly",
        Title: "Dragonfly cache",
        Readme: "Install with Helm (Dragonfly) and appsettings.template.json Memory:Dragonfly, ChatContext:AnswerCache",
        What: "Optional. Memory uses Dragonfly for a search cache and for write limits shared by every replica (never for cards); Chat can answer a " +
              "repeated first question from it (answer cache). Off: limits stay per process and nothing is cached.",
        Prerequisites:
        [
            "A Dragonfly or Redis: the chart's bundled one (dragonfly.enabled, the default; Service dragonfly:6379, password in Secret dragonfly-auth) or your own.",
            "More than one Skanyxx replica is the main reason to turn it on.",
        ],
        Settings:
        [
            new("Memory:Dragonfly:Configuration", "Memory's search cache is off and write limits are counted per replica.",
                "A StackExchange.Redis configuration string (host:port,password=…); the password comes from the dragonfly-auth Secret, never a committed file."),
            new("ChatContext:AnswerCache:Enabled", "Repeated first questions are always sent to the agent again.",
                "true, together with ChatContext:AnswerCache:Redis (the same kind of configuration string).", Optional: true, MustBeTrue: true),
        ],
        Local:
        [
            new("Run a Redis-compatible server on loopback, then start the Host with its configuration string (no password locally).",
                "Memory__Dragonfly__Configuration=127.0.0.1:6379 scripts/dev/first-hour.sh host"),
        ],
        Helm:
        [
            new("The chart does not set these yet: add them through skanyxx.extraEnv, reading the password from the Secret first so it never sits in values: " +
                "[{name: DRAGONFLY_PASSWORD, valueFrom: {secretKeyRef: {name: dragonfly-auth, key: password}}}, " +
                "{name: Memory__Dragonfly__Configuration, value: \"dragonfly.<namespace>.svc:6379,password=$(DRAGONFLY_PASSWORD)\"}]."),
            new("For the answer cache add ChatContext__AnswerCache__Enabled=true and ChatContext__AnswerCache__Redis the same way, then upgrade.",
                "helm upgrade skanyxx deploy/helm/skanyxx -n <namespace> --reuse-values -f <your-values.yaml>"),
            new("The NetworkPolicy already admits Skanyxx to Dragonfly. Never point AX at an LRU-evicting instance (AX needs noeviction)."),
        ],
        Verify:
        [
            new("The Host starts (an invalid configuration string stops it with the setting's name), and repeated searches stop reaching Postgres."),
        ],
        Errors:
        [
            new("\"Memory:Dragonfly:Configuration is not a valid Redis configuration string.\"", "Write it as host:port[,password=…]."),
            new("\"ChatContext:AnswerCache:Enabled needs ChatContext:AnswerCache:Redis…\"", "Set the Redis configuration string or turn the answer cache off."),
            new("Dragonfly stops answering", "Treated as off until it answers again; check the pod and its password."),
        ]);
}
