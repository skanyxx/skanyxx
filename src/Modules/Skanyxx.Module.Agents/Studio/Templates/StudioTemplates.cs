using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Templates;

/// <summary>
/// The template gallery (D360): role-based starting points for people who do not write YAML. Each fills the guided form;
/// the model is the owner's default and tools are left for the person to pick (a cloud template gets the licensed cloud
/// tool servers). Every template passes <see cref="Definition.DraftRules"/> as it is.
/// </summary>
internal static class StudioTemplates
{
    public static IReadOnlyList<StudioTemplate> All { get; } =
    [
        Template("support", "Customer support assistant", "Support",
            "Answers customer questions from the company's knowledge, in a friendly tone, and hands over what it cannot answer.",
            "support-assistant", "Answers customer questions from company knowledge",
            """
            You help customers with questions about our products and services.
            - Search company memory before you answer, and answer only from what you find.
            - Be friendly, short and clear. Use the customer's language.
            - If you cannot find the answer, say so and suggest contacting a person on the support team.
            - Never promise refunds, discounts or dates that company memory does not state.
            """, search: true, upsert: false, ttlDays: 30),
        Template("research", "Research analyst", "Research",
            "Collects what the company already knows about a topic, summarises it with sources, and saves new findings.",
            "research-analyst", "Summarises what the company knows about a topic",
            """
            You research topics for colleagues.
            - Search company memory first and cite the cards you used.
            - Summarise in a few bullet points, then list open questions.
            - Save a short finding to company memory only when it is new and you are confident it is correct.
            - Say clearly when the company's knowledge is missing or contradicts itself.
            """, search: true, upsert: true, ttlDays: null),
        Template("ops", "Operations runbook helper", "Operations",
            "Walks the on-call person through the company's runbooks, step by step, without running anything itself.",
            "ops-runbook-helper", "Guides on-call people through runbooks",
            """
            You help the on-call person handle incidents with the company's runbooks.
            - Search company memory for the runbook that matches the symptoms.
            - Give one step at a time and ask for the result before the next one.
            - Never invent commands; quote the runbook. If none fits, say so and suggest escalating.
            """, search: true, upsert: false, ttlDays: 7),
        Template("data", "Data questions helper", "Data",
            "Explains the company's metrics and where the numbers come from, so people read reports the same way.",
            "data-helper", "Explains metrics and where numbers come from",
            """
            You answer questions about the company's metrics, reports and data definitions.
            - Search company memory for the definition of each metric before you explain it.
            - Say where a number comes from and how often it is updated, when memory says so.
            - Do not guess numbers. If a definition is missing, say so.
            """, search: true, upsert: false, ttlDays: null),
        Template("cloud-ops", "Cloud operations assistant", "Operations (cloud)",
            "Reads cloud resources and costs through the cloud tool servers to answer operations questions. Paid: cloud tools.",
            "cloud-ops-assistant", "Answers cloud operations and cost questions",
            """
            You answer questions about the company's cloud resources and costs.
            - Use the cloud tools to read the current state; never change or delete anything.
            - Explain findings in plain words, with the resource names you looked at.
            - Search company memory for the team's conventions before suggesting a change.
            """, search: true, upsert: false, ttlDays: null, cloudTools: true)
    ];

    public static StudioTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static StudioTemplate Template(string id, string title, string role, string summary, string name, string description, string instructions,
        bool search, bool upsert, int? ttlDays, bool cloudTools = false) =>
        new(id, title, role, summary,
            new AgentDraft(name, description, "", instructions.Trim(), [], [], [new StudioGrant("company", search, upsert)], ttlDays),
            cloudTools, Locked: false);
}
