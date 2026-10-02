# Memory card (deep: keep it tiny)

The card is what search returns and what the model sees. If this grows, token cost grows. Body and source stay out of the default prompt (`memory-store.md`).

## Fields (locked)

| Field | On the prompt? | Why |
|---|---|---|
| `collection` + `key` | yes (key is short) | Identity for **upsert** (D015). Same key = same fact, overwrite. |
| `type` | yes | `decision` \| `fact` \| `procedure` \| `open` |
| `what` | yes | One line. Max **200** characters. |
| `why` | yes | Short reason. Max **400** characters. |
| `who` | yes | Person (and agent, if an agent wrote it). |
| `when` | yes | Timestamp. |
| `status` | no* | `candidate` \| `published` \| `stale`. Agents search **published** by default. |
| `body` | **no** | Extra for the library. |
| `source` | **no** | Pointer: chat id / file / URL. Not the blob. |

\*Status can be shown in the library; do not spend prompt tokens on `stale`/`candidate` unless someone asks.

`id` exists for internals. Agents and humans think in **`collection/key`** (like `billing/refund-window`).

## Types (only these)

| Type | Example `what` |
|---|---|
| `decision` | We refund within 14 days |
| `fact` | Warehouse closes at 16:00 |
| `procedure` | How to file a refund in the ERP |
| `open` | Still deciding whether EU refunds differ |

No extra types. A recording is a **source**, not a type.

## Upsert

Writer (human or agent with **upsert** grant) sends `collection` + `key` + `version` + new `what`/`why`. Same key, new version replaces. Stale version = **conflict**, not a second card (D041). Humans may rename `key` in the library (D038).

## Prompt budget (one turn)

Top-k **published** cards from **search-granted** collections. k is small (start at **5**). Worst case ~5 × (200+400) characters, not the bank.
