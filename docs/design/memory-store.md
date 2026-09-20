# Memory store (volume + tokens)

Problem: if every employee’s every agent session is stored as text, storage and token cost both explode.

## What is stored

| Layer | What | Where it is used |
|---|---|---|
| **Card** | type, one-line what, short why, collection, who, when | Search index + agent prompt (top-k only) |
| **Body** | extra detail if a human opens the item | Library UI, not default prompt |
| **Source** | pointer to chat id / file / recording | Audit; fetched only if someone asks |

A session of 10k tokens should produce **0–few cards**, not 10k tokens in the bank.

## What is not stored as memory

- Full agent transcripts (unless a separate short-lived audit log)
- Every tool call, every draft
- Media itself (only the pointer)

## How an agent uses it

1. User asks a question  
2. Memory MCP: search **allowed collections**, return **k small cards** (not the corpus)  
3. Those cards go into this turn’s context  
4. If something new is locked, **upsert** one card (D015), do not append the chat  

That is the same pattern as this design log: we keep D012, we do not keep the whole thread in every later prompt.
