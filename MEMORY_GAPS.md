# Memory gap audit (branch `memory-impl`, 2026-10-27)

Every rule in `docs/design/memory-*.md`, plus the memory parts of `requirements.md` (§0 O1/O7, §3.2, §3.4, §3.6, §3.7),
checked against `src/Modules/Skanyxx.Module.Memory` (below: `M/`) and its callers: Chat, Tickets, Sandboxes, MCP.
Status is **before** this branch. The "After" column is what this branch changed (see `MEMORY_REPORT.md`).

Paths: `M/` = `src/Modules/Skanyxx.Module.Memory/`, `Chat/` = `src/Modules/Skanyxx.Module.Chat/`,
`Tickets/` = `src/Modules/Skanyxx.Module.Tickets/`.

## memory-card.md

| Spec line | Status | Where | After |
|---|---|---|---|
| Fields `scope`+`key`, `type`, `what`, `why`, `who`, `when`, `status`, `body`, `source` | done | `M/Domain/Card.cs`, `M/Data/CardConfiguration.cs` | + `tickets`, `links`, `keywords`, `origin`, `agent_id`, `superseded_by`, `created_at` |
| `what` ≤ 200, `why` ≤ 400 | done | `M/Domain/CardLimits.cs`, `UpsertCardValidator`, DB `HasMaxLength` | unchanged |
| Types only decision/fact/procedure/open | done | `CardType`, `ck_memory_cards_type` | unchanged |
| `status` candidate/published/stale; agents search published by default | partial: every write is `published` (`UpsertCardHandler` TODO D011), nothing ever sets `candidate` or `stale` | `M/Features/Cards/UpsertCardHandler.cs:171` | done: review scopes make agent writes candidates; status endpoint publishes/marks stale |
| body and source never on the prompt | done | `CardSearch` selects no body/source; `CardHit` | unchanged; injection uses the same fields |
| Upsert by `scope`+`key`+`version`; stale version = conflict | done | `UpsertCardHandler`, `IsConcurrencyToken` | unchanged |
| Rename key (people only) | done | `RenameCardHandler` (D038, D102) | records history |
| Prompt budget: top-k (5) published from search-granted scopes | done for MCP search (`SearchCardsHandler`, `SearchTopK`) | | + token budget for chat injection |
| Large bodies to the object store, pointer in `source` | done | `M/Bodies/CardBodies.cs` (D180–D184) | unchanged |

## memory-hierarchy.md

| Spec line | Status | Where | After |
|---|---|---|---|
| Layers personal → team → department → company | done | `Scope`, `ScopeLevel` | unchanged |
| "Chats live in personal; cards are born there" — extract cards in a personal space | **missing**: nothing turns a chat or run into a card; only an agent's explicit `memory_upsert` | — | done: chat turns and finished runs write one small personal card each |
| What is lifted: cards yes, transcripts no | done (no transcript is ever stored) | | extraction stores ≤ 200+400 chars, a pointer, ticket keys |
| Promote = copy up; original stays | done | `LiftCardHandler` | lift also copies tickets/keywords, drops links into lower scopes, records `lifted_from` |
| Lift upward only | done | `LiftCardValidator` | unchanged |
| Lift into team: a member; into company: supervisor/owner | done | `AccessPolicy.CanUpsertAsync` | unchanged, now covered for department too |
| Employee + seed upsert defaults to personal | done | `AccessPolicy.DefaultGrants` (D078) | unchanged |
| Library visibility per scope; agents only by grant; team grants owner-only | done | `AccessPolicy`, `SetAgentGrantsHandler` (D090/D091) | unchanged |

## memory-how.md

| Spec line | Status | Where | After |
|---|---|---|---|
| Session vs card never mixed; 10k-token chat → few cards | done by omission (no extraction at all) | | extractor caps the card, never stores the transcript |
| Session TTL 14 days | n/a here: transcripts live in kagent; the chat index is the chat-context branch | | unchanged |
| §3.1 Search: top-5 published cards into **this turn only** | partial: only if the agent calls `memory_search` itself | `MemoryTools.SearchAsync` | done: the first turn of a new chat gets a budgeted block of related cards (user-readable ∩ agent-searchable) |
| §3.3 Lock: upsert personal | done | `MemoryTools.UpsertAsync` | + optional `tickets`, `links` arguments |
| §4 Lift via library to team, then supervisor to company | done (REST + `/Library`) | `LiftCardEndpoint`, `Pages/Library` | unchanged |
| §5 New agents: search company, no upsert | done (D078 refines) | | unchanged |
| §5 Preview agents must not upsert | done in studio (preview grants) | Agents module | unchanged |
| §5 Two writers same key: serialize, conflict, no LWW | done | version concurrency token + unique index | unchanged |
| §5 One Dragonfly may cache/lock this service | **missing** | — | done: optional `Memory:Dragonfly` (search cache + shared write limits), in-process fallback |
| §5 Rate limit upserts per agent | done (in-process) | `UpsertRateLimiter` | shared across replicas when Dragonfly is on |
| §6 Library per person / team / company | done | `BrowseLibraryHandler` | + ticket/keyword/type/origin filters |
| §7 Postgres FTS day one, vectors later | done | `CardSearch` | + ticket-key match; pgvector still off (design note added) |

## memory-stack.md

| Spec line | Status | Where | After |
|---|---|---|---|
| Postgres: unique (scope,key), version, FTS | done | `CardConfiguration` | + GIN on tickets/keywords |
| Versions | partial: only the current version is kept | | done: `memory_card_history` keeps every replaced version; `GET …/history` |
| S3/MinIO for blobs | done | `CardBodies` | unchanged |
| Dragonfly cache/locks only, never the card store | missing | | done (off by default) |
| pgvector later | missing (by design) | | still off; `memory-stack.md` names the column and index it would add |

## memory-store.md

| Spec line | Status | Where | After |
|---|---|---|---|
| Card / body / source layers | done | | unchanged |
| Not stored: transcripts, every tool call, media | done | | extraction keeps only the first line of the question and a short outcome |
| Agent use: search allowed scopes, k small cards, upsert one card when locked | done | `MemoryTools` | unchanged |
| Volume limit (implied: "storage explodes") | **missing**: no cap per scope | | done: `MaxCardsPerPersonalScope`, `MaxCardsPerScope` |
| Token limit on what reaches a prompt | partial (top-k only) | | done: `Memory:Injection:MaxTokens` |

## memory-writes.md

| Spec line | Status | Where | After |
|---|---|---|---|
| Most agents must not write (default search only; D078 refinement) | done | `AccessPolicy` | unchanged |
| Lock on the key, conflict on old version | done | | unchanged |
| Redis/Dragonfly not a bus, not the source of truth; optional cache/limits/locks | missing | | done as cache + shared counters |
| Upsert rate limit per agent | done | `UpsertRateLimiter` | unchanged in-process; Dragonfly-backed when on |
| Same agent + same key: coalesce, don't write 50×/min | **missing** | | done: identical content is a no-op (no new version); `UpsertsPerKeyPerMinute` per caller+key |

## requirements.md (overrides)

| Requirement | Status | Where | After |
|---|---|---|---|
| O1/§3.4: on the first message of a new chat, related cards the user may read are injected within a strict token budget; response lists them | missing | `Chat/Features/SendChatMessageHandler.cs` sends the message as-is | done for **cards** (`IRelatedMemory`); related **sessions** are the chat-context branch |
| O1: chat index (keywords, categories, ticket links, summary) | missing | | **not here**: chat-context branch. Memory exposes `ICardExtractor` / `IRelatedMemory` for it |
| §3.4: user B never gets A's private session or card | done for cards in REST/MCP | `AccessPolicy` | injection + extraction covered by tests |
| §3.2: a finished run writes a card in the run owner's scope, liftable | missing | `Tickets/Engine/RunStepper.cs` | done: `TicketRunFinished` notification → personal card |
| §3.6: hierarchy, card/body/source, lift, **supersede**, versions, grants, MCP | partial: no supersede, no version history | | done |
| §3.6: Dragonfly answer cache | missing | | **not here**: chat-context branch owns the answer cache (its D241) |
| §3.7: Library filters scope/team/dept/owner/agent/ticket/keyword/date/type, sort, paging | partial: scope + text, 50 rows | `BrowseLibraryQuery` | partial: + ticket, keyword, type, origin, agent filters on the API; UI paging/saved filters still open |

## Callers

| Caller | Status before | After |
|---|---|---|
| Chat | no memory use at all | first-turn injection + per-turn session card (via Core interfaces, no-op without memory) |
| Tickets | no memory use | finished run → `TicketRunFinished` (Core notification) → memory |
| Sandboxes | sandbox token → `/mcp/memory` as owner (D190–D194) | unchanged |
| MCP | `memory_search`, `memory_upsert` | `memory_upsert` takes `tickets`/`links`; search also matches ticket keys |
