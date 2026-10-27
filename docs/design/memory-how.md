# How memory works (end to end)

This is the company brain. kagent’s built-in memory is **not** this: that one is per-agent, ~15 day TTL, no sharing. Ours is scoped, durable, and granted.

## 1. Two kinds of data (never mixed)

| | Session (chat) | Card (memory) |
|---|---|---|
| What | The conversation | One locked what + why |
| Where | Personal only | Personal, then maybe lifted |
| Size | Can be huge | `what` ≤ 200 chars, `why` ≤ 400 |
| Life | TTL **14 days** (owner can change) | Until superseded / stale |
| In the model prompt? | Current turn only | Top-5 **published** hits |

A 10k-token chat must not become 10k tokens in the bank. At most a **few cards** if something actually locked.

## 2. Where a card lives (`scope` + `key`)

```
personal:{user}     chats + new cards are born here
       ↓ lift (copy of the CARD, not the transcript)
team:{id}
       ↓ lift
department:{id}
       ↓ lift (supervisor / owner)
company               most agents SEARCH here
```

Same slug can exist at two scopes: `personal:ana/refund-window` and `company/refund-window`. The fleet reads **company**. Ana still has her original.

Identity for overwrite is **`scope` + `key`** + **`version`**. Not “looks similar.”

Org tree (teams/depts) is **in Skanyxx**. Entra is optional mapping. No Azure required.

## 3. One employee turn (the common path)

Ana opens Skanyxx (browser or desktop client). She talks to the **seed** agent. Client → Skanyxx API / kagent URL. kagent runs the model.

1. **Search** — Memory MCP: FTS on scopes this agent may **search**. Seed may search `company` (and Ana’s personal). Returns **top 5 published cards**. Those strings go into **this turn only**. The bank is not dumped into the prompt.
2. **Answer** — usual case: **write nothing**.
3. **Lock** — they agree “we refund in 14 days because policy X.” Seed has **upsert on personal**, not on company. MCP `upsert(scope=personal:ana, key=refund-window, type=decision, what=…, why=…, version=new)`.
4. Postgres unique `(scope,key)`, bump version. Blob (if they attached a PDF) goes to MinIO; card stores a **pointer**.

kagent TTL memory may still store “Ana likes short answers.” That dies in ~15 days and never becomes company knowledge.

## 4. Lift — how private chats stop being the only brain

The transcript stays personal and will expire. The **card** can be copied up:

- Ana (or a teammate) in the **library**: “Lift to team Billing.”
- Copy: new row `team:billing/refund-window`. Ana’s personal card remains.
- Later, supervisor: “Lift to company.” New row `company/refund-window`.
- Agents with **search company** now see it. They never needed Ana’s chat log.

Small org: skip team/dept; lift personal → company still needs **supervisor/owner**. That gate is the same idea as merging agent YAML.

## 5. Many agents

New specialist agents default to **search `company`**, **no upsert**. Write is an explicit grant (search and upsert are **separate** per scope).

Preview (draft agent, unmerged PR): may **search** production; **must not upsert**.

Two writers on `company/refund-window` at once: engine serializes that key. First wins, version++. Second with old version → **conflict**, re-search. No silent last-write-wins. No Redis bus for agents. One Dragonfly (shared with AX) may cache/lock this service (D068).

Rate limit upserts per agent so a loop cannot flood Postgres.

## 6. Humans in the library

- Ana sees her personal cards + chats (until TTL).
- Billing team sees `team:billing` published cards.
- Any logged-in user can browse **published `company`**.
- They do not need an agent to *read* the trail; they need an agent to *use* it in a turn.

## 7. What actually runs (Helm umbrella)

Default bundled, or their RDS/S3/GitHub (D061):

```
kagent  --MCP-->  memory service  -->  Postgres (cards, sessions, FTS, versions)
                                  -->  MinIO/S3 (blobs)
desktop/browser  -->  Skanyxx API (who may search/upsert/lift)
                 -->  kagent (the turn)
```

Day one: **FTS**, not embeddings. Cards are small; vectors are a later index. Joicy is **not** this database; we stole the lift idea only.

## 8. What memory is not

- Not the chat history
- Not kagent’s per-agent TTL store
- Not “record into one agent”
- Not a file explorer of disks
- Not something every agent may write

## As built (2026-10-27, memory-impl, D301–D307)

- **Turn 1 of a new chat:** Skanyxx (not the agent) searches for related published cards in scopes the person may read
  *and* the agent may search, and prepends at most 5 whole cards within ~600 tokens. The reply lists them
  (`memoryCards`). Later turns rely on `memory_search`.
- **After every answered turn:** the session's personal card is created or updated (coalesced: an unchanged card is not
  rewritten). A finished ticket run writes its own card.
- **Many writers:** identical updates are no-ops; one caller may write one card at most 6 times a minute; per caller and
  total limits as before. With `Memory:Dragonfly` those counters and a search cache are shared by every replica; without
  it (or while it is down) memory runs exactly as day one.
- The chat **index** (related sessions, answer cache) is the chat-context branch; it calls the same `IRelatedMemory` /
  `ICardExtractor` (Core) instead of the Chat handler doing it.
