# Seed + factory with PR (locked)

kagent agents are YAML (CRDs). We use that.

| Job | What happens | Approval |
|---|---|---|
| **Seed** | Talk + upsert **memory cards** | Collection policy (D011). **No git PR.** |
| **Factory** | Chat proposes **agent YAML** (and related grants) | **Git PR. Live only after merge.** |
| **Studio** | Same YAML artifacts, form UI instead of chat | Same PR (or same repo); not a second source of truth |

Factory is **sugar for builders/devs**, not for every employee. The seed agent employees talk to does not open PRs. Builder role gets a “propose agent” path (same general agent with extra tools, or studio).

## Factory flow

```
Builder: “Make a Refund agent, billing collection, this MCP”
    → Skanyxx/kagent writes YAML on a branch
    → opens a PR against the agent-config repo (created at setup, D023)
    → **Agent supervisor** reviews YAML and merges (D024)
    → kagent applies the Agent CR
```

Nothing talks to the live cluster as “create agent” from a single chat turn. Chat drafts; git is the gate.

## Must not go through PRs

Memory cards. A PR per “we refund in 14 days” would drown the repo and fight D012–D013.

## Same files from two doors

Studio and factory both produce the repo. Git is the source of truth for **agents / MCP / skills / grants**. Skanyxx reads that repo; it does not keep a silent second fleet.
