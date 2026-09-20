# Step 3 — Agent studio

Two doors, one repo, one gate (D020):

| Door | Who | What they do |
|---|---|---|
| **Studio** | Builder | Form: compose an agent, **Save draft → PR** |
| **Factory** | Builder | Chat: “make a Refund agent…”, same files, same PR |
| **PR merge** | Agent supervisor | YAML review; then it is live in kagent |

Studio is **not** a live kubectl apply button. It is not kagent’s dashboard. Primary UI is the form; YAML is what the supervisor reads.

## What a builder composes (this is the form)

Maps to a kagent `Agent` CR + Skanyxx grants in the same PR:

| Field | Plane | Notes |
|---|---|---|
| Name, description | kagent | Who this agent is in chat |
| Model | kagent `ModelConfig` | Pick one the owner already configured |
| Instructions | kagent `systemMessage` | The job, tone, what it must not do |
| Skills | kagent `skills.refs` / git / A2A | Templates later (step 5); attach now |
| MCP tools | kagent `RemoteMCPServer` + `toolNames` | Which servers, which tools |
| Collection grants | **Skanyxx** | Per collection: **search** and/or **upsert** (D033). Preview never upserts (D032). |
| kagent TTL memory | kagent, optional | Per-agent leftovers. **Not** the company bank (D008) |

If they add a **new** MCP server or skill ref, that YAML is in the **same PR**. Supervisor sees the whole change.

The seed general-purpose agent is just the first file. Changing it uses the same studio + PR path.

## What studio is not

- Not editing memory cards (library / chat seed does that)
- Not inviting users (owner)
- Not merging (supervisor)
- Not a YAML IDE as the happy path (advanced toggle later is fine)

## Preview vs production (locked)

- **Preview chat** on the branch (D030): builder/supervisor only.
- **Employee chat** lists **merged** agents only (D031).
- Preview may **search** granted collections so retrieval can be tested; it **must not write** cards to production (D032).
