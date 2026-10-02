# Step 1 — Three planes (deep)

One customer license = one Skanyxx. Inside that install there are **three planes**. Mixing them is how you get petabyte logs, token burn, and a second kagent dashboard.

```
Employee / owner / builder
        │
        ▼
┌───────────────────┐
│  SKANYXX          │  workspace (the product)
│  people, roles    │  author agents, grant collections,
│  library, studio  │  review queue, community import
└─────────┬─────────┘
          │ declares: this agent, these tools, these collections
          ▼
┌───────────────────┐
│  KAGENT           │  runtime (not the product)
│  model + prompt   │  runs the loop, MCP, skills, A2A
│  + tools + skills │  kagent’s own memory stays per-agent + TTL
└─────────┬─────────┘
          │ MCP search / upsert of *cards only*
          ▼
┌───────────────────┐
│  MEMORY ENGINE    │  company knowledge
│  collections      │  cards in the index; bodies for humans;
│  cards + policy   │  sources as pointers (D012–D015)
└───────────────────┘
```

Joicy is a **shape** for the bottom plane (memory as MCP). It is not Skanyxx, and it is not kagent.

## Who owns what

| Plane | Owns | Must not own |
|---|---|---|
| **Skanyxx** | Users, roles, agent *definitions*, collection grants, library UI, publish policy, template catalog | Running the LLM; storing every chat as “knowledge” |
| **kagent** | Executing one turn: model, skills, MCP tool calls | Company-wide knowledge; Skanyxx UX |
| **Memory** | Cards, collections, search, upsert/supersede | Full transcripts; the agent runtime |

kagent already has a UI. We still do not compete with it (facts.md). Skanyxx is where the **company** lives.

AX is a **fourth** optional plane for **Tasks** only (`ax-tasks.md`). It does not replace kagent for chat.

## One turn (this is the product loop)

1. Person talks to an agent (where that chat UI lives is still open).
2. kagent runs the model. Before answering, the agent **searches** memory MCP → **top-k cards** from **granted collections** (D013).
3. Answer happens. Almost every turn **writes nothing**.
4. Only when something is *locked* (a decision, a fact, a why) the agent **upserts one card** (D010, D015).
5. A later person can ask another agent, or open the **library** in Skanyxx (D009).

That is the same thing we are doing in `docs/design/` by hand.

## Two different “memories” (do not merge)

| | kagent built-in | Company bank |
|---|---|---|
| Scope | one agent | collections, many agents |
| Lifetime | ~15 days TTL | until superseded / policy |
| Content | chat leftovers | locked what + why |
| Sharing | **none** | the whole point |

We may still leave kagent memory **on** for “this user prefers short answers.” That is not the company brain.

## Locked on this step

- Talk surface: **Skanyxx chat only** (D017).
- We **provide the setup** (D018): Skanyxx + kagent + memory, one stack.
