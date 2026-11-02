namespace Skanyxx.Host.Setup.Guides;

/// <summary>Investigation workflows over the kagent agents Chat offers (D340, D557). Facts: README "Investigate".</summary>
public static class InvestigateGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "investigate",
        Title: "Investigate",
        Readme: "Investigate",
        What: "Builders, supervisors and the owner build workflows of agent steps (one after another or at the same time) and run them on a question. " +
              "Each run reports every step's answer, downloads as JSON or PDF, and leaves a memory card with the finding in the starter's personal scope.",
        Prerequisites:
        [
            "Chat working (see the Chat guide): a step can use any agent Chat offers (every kagent agent except Studio previews, the factory, ticket stages and suspended ones; D557).",
            "Memory working (see the Memory guide): workflows and runs are kept in memory's database, and the finding becomes a card.",
            "People who build workflows hold the builder or supervisor role; employees do not get the panel.",
        ],
        Settings:
        [
            new("KAgent:BaseUrl", "No agent can be listed or asked, so no workflow can be saved or run.",
                "localhost (with KAgent:Port 8083) through the port-forward locally; the chart sets KAgent:IngressUrl.", Alternative: "KAgent:IngressUrl"),
            new("ConnectionStrings:Memory", "Skanyxx does not start: workflows and runs live in memory's database.", "See the Memory guide."),
            new("Investigate:StepTimeoutSeconds", "A step may take up to 300 s before it fails the run.",
                "Seconds, 5 to 3600, e.g. Investigate__StepTimeoutSeconds=900 for slow local models.", Optional: true),
            new("Investigate:MaxActiveRunsPerUser", "Each person may have 3 investigations queued or running at once.",
                "1 to 1000; Investigate:MaxActiveRuns caps everyone together (20).", Optional: true),
            new("Investigate:ReadOnlyToolServers", "Every MCP tool server counts as able to write: a step that reads an earlier answer cannot call an agent with tools unless a supervisor allows chained writes on that workflow.",
                "The RemoteMCPServer names whose tools only read, e.g. Investigate__ReadOnlyToolServers__0=docs-search.", Optional: true),
        ],
        Local:
        [
            new("Keep kagent's port-forward up and the seed agent applied.", "scripts/dev/first-hour.sh status"),
            new("Run the Host and sign in as the owner, a supervisor or a builder.", "scripts/dev/first-hour.sh host"),
            new("Open /Investigate → New workflow: add an agent step, pick the seed agent, write a prompt with {{input}}, save, then Run."),
        ],
        Helm:
        [
            new("Nothing to install: the module ships in the image and uses memory's database. Tune it through skanyxx.extraEnv (Investigate__StepTimeoutSeconds, Investigate__MaxActiveRuns)."),
            new("The network policy already lets Skanyxx call kagent's controller; no new egress is needed."),
        ],
        Verify:
        [
            new("Run a one-step workflow: the step goes running → succeeded, Report (PDF) downloads, and Memory card opens the finding in /Library."),
            new("A builder signed in sees only their own workflows and those shared with their teams; an employee gets 403 on /api/investigate."),
        ],
        Errors:
        [
            new("\"kagent did not answer\" on the page", "kagent's controller is not reachable from the Host: check the Chat guide."),
            new("409 \"Not an agent you can use now\"", "A step names an agent that was suspended or removed from kagent: pick another agent and save."),
            new("409 \"… can write (tools or memory)\"", "A step that reads an earlier answer calls a write-capable agent: use a read-only agent, list its servers in Investigate:ReadOnlyToolServers, or have a supervisor tick Allow chained writes."),
            new("A step fails \"timed out after N s\"", "The agent took longer than Investigate:StepTimeoutSeconds: raise it, or split the step."),
            new("429 on Start", "Too many investigations queued or running: wait for one to finish or cancel it."),
            new("A run fails \"The server running this investigation stopped\"", "The Host restarted mid-run; run it again."),
        ]);
}
