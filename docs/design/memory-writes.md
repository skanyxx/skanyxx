# Many agents, one bank (writes)

100 agents “storming” the bank is mostly a **product** bug, not a missing Redis.

## 1. Most agents must not write

Default: **search only** on `company` (D040). Upsert is a supervisor/builder grant, like giving deploy keys.

If 95 agents only search, they cannot race. They also cannot burn write-amp.

## 2. Writers still need a lock — on the **key**, not the fleet

Two upserting agents can still hit `billing/refund-window` at once.

```
Agent A and Agent B both MCP upsert same collection/key
        → memory engine takes that key in order
        → first write succeeds, bumps version
        → second write with old version → CONFLICT
        → second agent searches again, then decides (or stops)
```

No silent last-write-wins (D041). The engine is the orchestrator of **cards**, not of agents.

## 3. Redis / Dragonfly — what they are not

| Use | Yes? |
|---|---|
| Bus to “orchestrate 100 agents” | **No.** That is kagent / A2A. |
| Source of truth for cards | **No.** Need durable store + unique `(collection, key)`. |
| Later: hot cache of top-k cards, rate limit, lock if **several memory replicas** | Optional. Day one: one memory engine + DB constraint + `version` is enough. |

One **Dragonfly** is on by default (D068) because AX needs Redis-protocol. Memory may use that same instance for cache/locks. It does not replace D040–D041. Cards stay in Postgres.

## 4. Extra brakes (server-side)

- Upsert rate limit per agent (a maniac loop dies at the MCP, not in the table)
- Same agent + same key: coalesce, don’t write 50 times a minute
