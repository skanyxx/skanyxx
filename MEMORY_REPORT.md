# Memory bank — implementation report (branch `memory-impl`, 2026-10-27)

Gap audit: `MEMORY_GAPS.md` (status before this branch, per spec line). Decisions: `docs/design/decisions.md` D300–D308.
Each `memory-*.md` has a short "As built (memory-impl)" section.

## What was built

| Area | What | Decision |
|---|---|---|
| (a) Extraction | `ICardExtractor` (Core). Each chat session is **one** card `personal:{user}/chat-{session}` (first question as `what`, gist of the latest answer as `why`, tickets from every turn, keywords, `source = chat:{id}`, `origin = chat`, `who = "user via agent"`). A finished ticket run (succeeded/failed) is `personal:{owner}/run-{ticket}-{run}` with the ticket, tickets it mentions, its URL. Deterministic cuts, no model call, no transcript. Small talk keeps nothing. | D301 |
| (b) Injection | `IRelatedMemory` (Core). On a new chat's first turn: published cards from (person's scopes **without** oversight) ∩ (agent's search grants), ticket matches first, top 5, whole cards within 600 estimated tokens; labelled as reference notes. `ChatResponse.MemoryCards` lists them. | D302 |
| Card links | `tickets`, `links`, `keywords`, `origin`, `agent_id`, `superseded_by`, `created_at`; search matches ticket keys and keywords; `memory_upsert` takes `tickets`/`links`. | D303 |
| (c) Lift / publish | Lift was already upward-only and gated; it now copies tickets/keywords/origin and drops links into lower scopes. New: `POST …/status` (publish / stale / supersede / revive), `GET …/history`, `Memory:ReviewScopes` (agent writes become candidates). | D304 |
| (d) Multi-writer | Identical update = no write; per caller+card limit; create-on-existing stays a conflict; a lost race on the history index is a conflict, never a 500. | D305 |
| (e) Dragonfly | `Memory:Dragonfly` (off by default): search cache (exact scope set + per-scope generations), write counters shared by replicas; fail-fast, in-process fallback, startup validation. | D306 |
| (f) Limits | Per-scope card quotas; extraction evicts the oldest extracted card, never a hand-written one; token budget for injection; caps on tickets/links/keywords. | D307 |
| Library | `LibraryFilter` (ticket, keyword, type, origin, agent, offset) + `GET api/memory/library`. | D308 |

## Callers

- **Chat** (`SendChatMessageHandler`): calls `IRelatedMemory` before the first turn and `ICardExtractor` after every
  answered turn. No-op implementations registered with `TryAdd`, so Chat works without memory.
- **Tickets**: `RunStepper` and `DecideGateHandler` publish `TicketRunFinished` (Core notification) after a terminal save;
  memory's `RememberTicketRun` handles it. Cancelled runs publish nothing.
- **Sandboxes / MCP**: unchanged access; `memory_upsert` gained optional `tickets`/`links`.

## Merging with chat-context

The chat-context branch owns `IChatContext` (Prepare/Completed), the session index and the answer cache. On merge:

1. `SendChatMessageHandler`: keep chat-context's `IChatContext` flow; drop this branch's direct calls. Its
   `ChatContextService.PrepareAsync` should take cards from `IRelatedMemory.ForNewChatAsync` (this branch's access rule
   is stricter: user-readable ∩ agent-searchable), and `CompletedAsync` should call `ICardExtractor.ChatTurnAsync`.
2. `ChatResponse`: both branches add a property (`RelatedCards` there, `MemoryCards` here) — keep one.
3. `Skanyxx.Host.csproj`: both add `StackExchange.Redis 2.8.16` with different comments — keep one line.
   `PluginLoadContext.cs`: the same hunk on both sides.
4. `decisions.md`: chat-context uses D236+, this branch D300–D308 — concatenate.

## Files

- New (memory): `Coordination/` (Dragonfly, SearchCache, DragonflyConfiguration), `Sessions/` (CardExtractor,
  RelatedMemory, SessionText, RememberTicketRun), `Domain/` (CardOrigin, CardVersion, CardLinks), `Data/` (CardRow,
  CardVersionConfiguration, migration `20261027103213_CardLinksAndVersions`), status/history/library features and
  endpoints, `DragonflyOptions`, `InjectionOptions`, `ExtractionOptions`, `Features/Cards/CardWrites.cs`.
- New (Core `Platform/Memory`): `ICardExtractor`, `IRelatedMemory`, `ChatTurnDigest`, `TicketRunFinished`,
  `MemoryContext`, `MemoryCardRef`, `RelatedMemoryQuery`, `CardTickets`, `SetCardStatusCommand`, `CardHistoryQuery`,
  `CardVersionDto`, `LibraryFilter`.
- Changed: memory `Card`, `CardConfiguration`, `CardSearch`, upsert/lift/rename handlers, `UpsertRateLimiter`,
  `MemoryOptions`, `MemoryModule`, `MemoryTools`; Core `CardDto`, `LibraryEntry`, `BrowseLibraryQuery`, `ChatResponse`;
  Chat module + handler; Tickets `RunStepper`, `DecideGateHandler`, new `RunFinishedNotice`; Host csproj +
  `PluginLoadContext`; `appsettings.template.json`.

## Tests

New: `ExtractionTests`, `RelatedMemoryTests` (access control: another person's card never injected, team cards only for
members via a granted agent, oversight never reaches a prompt, published only, budget), `WriteRulesTests`,
`LiftChainTests`, `DragonflyTests` (Redis container: cache + invalidation, no cross-access, shared limits, fallback,
bad config) in Memory; `RunFinishedTests` in Tickets; two chat tests in Host `FirstHourTests` (first turn carries
cards, later turns do not, the session becomes a personal card; another person's session card is never injected).
Test infra: `Memory:UpsertsPerKeyPerMinute` raised to 1000 in `MemoryApp.Settings`, like the other write limits there.

Results: see the end of this file.

## Left / open

- `/Library` page UI for the new filters, status buttons, history view, paging and saved filters (§3.7).
- Helm: no value yet maps `Memory__Dragonfly__Configuration` to the bundled Dragonfly.
- Session card coalescing is "unchanged = no write" plus the per-card limit; a session with more than 6 changing turns
  a minute loses intermediate `why` updates (the last turn after a quiet minute wins).
- Extraction is deterministic; a model-based extractor can replace `CardExtractor` behind `ICardExtractor`.
- `Memory:ReviewScopes` is empty by default (D304); the owner may want `company` there.
- pgvector: designed in `memory-stack.md`, not built.
- The injected block is part of the user message kagent stores, so it shows in the kagent session history.

## Results (2026-10-27)

- `dotnet build Skanyxx.sln -c Release -p:UseSharedCompilation=false`: 0 errors.
- `dotnet test --solution Skanyxx.sln -c Release`: **2237 passed, 0 failed, 0 skipped** (Host, Identity, Memory,
  Sandboxes, Tickets). Memory alone: 415 (382 before this branch).
- No live kagent checks were run in this pass; a separate reviewer debugs next.
