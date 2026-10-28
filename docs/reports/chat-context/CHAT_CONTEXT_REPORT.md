# Chat context: report (branch `chat-context`, 2026-10-27)

Decisions **D236–D242** (`docs/design/decisions.md`), design note `docs/design/chat-context.md`, `memory-how.md` updated.
Owner requirements: `docs/design/requirements.md` O1, O7, §3.4, §3.6. That file appeared in the worktree during the work.
It is not part of these commits.

## What was built

| Requirement | Built |
|---|---|
| 1. Chat index in Postgres | New module `chatcontext` (`src/Modules/Skanyxx.Module.ChatContext`). Table `chat_sessions` lives in memory's database, with its own EF migration and history table (`__chatcontext_migrations`). FTS uses a stored tsvector plus GIN; `ticket_keys` also has a GIN index. |
| 2. Keyword/category/outcome extraction | `IChatExtractor`. The default is `DeterministicExtractor`, which makes no model call. It runs after every completed turn. |
| 3. Related context at chat start | On the first turn of a new conversation, the agent gets a fenced block of readable prior sessions and published company cards, capped at 600 tokens by default. Sessions match on the same ticket (given, or written in the message), a linked ticket, or overlapping words via FTS; shared categories raise the rank. `POST api/chat` returns `relatedSessions` and `relatedCards`. |
| 4. Ticket links | `ticketKeys` on `POST api/chat` (the Chat page's `?ticket=SDB-58`), keys found in the text, or `PUT …/tickets`. `GET api/chat/tickets/{key}` returns the ticket's sessions, its pipeline runs, and related tickets with their runs. Search: `GET api/chat/sessions?q=&ticket=&category=&agent=&mine=`. |
| 5. Answer cache | Built, off by default, on Dragonfly/Redis via StackExchange.Redis. Per-user keys; TTL of 30 minutes. Invalidated by: agent spec edit (`metadata.generation`), card writes (memory publishes `CardsChanged`), model save, a manual clear, or forgetting the session. Answers built on someone else's shared chat are never cached. "Ask again without the cache" refreshes an entry. A Redis that is down or slow counts as a miss. Hit and savings stats are exposed. |
| 6. Access control | One SQL "readable" filter is used on every read path. A personal session is its owner's only, with no supervisor or owner exception. Sharing follows memory's write rules. There are access tests for all of this. |
| 7. Continuity | The Chat page keeps the conversation per tab (`sessionStorage`). A reload reopens it, shows the history and continues the same kagent session. A History list (own sessions only) can reopen any earlier chat. |

### Schema (`chat_sessions`)

`session_id` PK (kagent id), `user_id`, `agent_ref`, `title` (≤120; the first line of the question, ≤80), `scope`
(`personal:{owner}` by default; `team:`, `department:` or `company` when shared), `keywords text[]`,
`keyword_weights jsonb` (≤64), `categories text[]`, `ticket_keys text[]` (GIN), `outcome` (≤400), `search_terms`,
`search tsvector` (generated: title A, terms B, outcome C; GIN), `turns`, `created_at`, `updated_at`. Indexes:
`(user_id, updated_at)` and `scope`. pgvector would add an `embedding` column next to `search` later; nothing depends on
it now.

### Flow

```
POST api/chat → Chat handler (Chat module, small change)
  new conversation + ChatReader → IChatContext.PrepareAsync (chatcontext)
     cache on? key(user, agent, agent metadata.generation, tickets, question, epoch, user generation) → hit: answer now
                (noCache: skip the read, the fresh answer replaces the entry)
     related sessions: readable, outcome ≠ ""; given or written ticket first, then linked ticket, then FTS rank (+ shared category)
     related cards: memory CompanyCardsQuery (published company only)
     RelatedBlock.Build → ≤ budget, whole items only
  kagent: create session, send block + message
  IChatContext.CompletedAsync → extractor → upsert row; first turn + cache on → keep the answer
     (not when the block held someone else's shared chat)
  response: answer + relatedSessions + relatedCards (+ cached)
```

### Where existing code was touched (kept small for the merge with `phase1-cleanup-rbac`)

- **Core:** new `Platform/ChatContext/*` and `Platform/Tickets/*`, plus `Platform/Memory/CompanyCardsQuery.cs` and
  `CardsChanged.cs`. `ChatResponse` gained 3 properties, `SendChatMessageCommand` 3 optional parameters, and
  `Agent.Revision` was added (one parse line in `KAgentApiClient`).
- **Chat:** handler (prepare/complete calls), request (`ticketKeys`, `noCache`), validator, a no-op `IChatContext`.
- **Memory:** `CompanyCardsQuery` handler and validator, the `AnnounceCardWrites` pipeline behaviour (3 registrations),
  and one comment line in `MemoryMigrator` (the lock-key list).
- **Tickets:** `ITicketRuns` implementation plus one registration.
- **Host:** `StackExchange.Redis` package, the PluginLoadContext shared list, and `Chat.cshtml` (continuity, history,
  `?ticket=`, the "used prior work" note).
- `appsettings.template.json` has a new `ChatContext` section.

### Configuration

```json
"ChatContext": {
  "RelatedTokenBudget": 600, "MaxRelatedSessions": 3, "MaxRelatedCards": 3,
  "Categories": {}, "TicketProjects": [],
  "AnswerCache": { "Enabled": false, "Redis": "", "TtlMinutes": 30 }
}
```

## Design changes made during the work

- The first version passed memory's `LibraryUser` from Chat into chatcontext and read the person's personal cards
  through `BrowseLibraryQuery`. Memory's architecture test `OnlyTheHostAndMemory_UseLibraryUser` failed: only the Host
  and memory may act as a person in memory. I did not change that test. The redesign:
  - chat code uses its own `ChatReader`;
  - injected cards are company-only, through an identity-free `CompanyCardsQuery` (the agent still finds the person's
    own cards through its own MCP search);
  - card-based cache invalidation became a `CardsChanged` notification that memory publishes after upsert, lift and
    rename. This replaced a per-request "newest card" fingerprint.
- The keyword weights (jsonb dictionary) needed a value converter. Npgsql refuses dynamic JSON. The tests caught it.
- **Review fixes:** an isolated code review and a verifier ran (task dir `review/`); everything material is fixed in
  `7fc3b72`.
  - **Redis timeouts.** The client reports a timeout as a `TimeoutException`, which Chat treats as kagent failing. A
    slow cache would have returned 503 and deleted a session kagent had already answered. Timeouts now count as a cache
    miss, with 1 s timeouts and fail-fast.
  - **Answers built on shared chats.** If a share was withdrawn, a cached answer could still carry it. These answers
    are no longer cached.
  - **Ticket project as a keyword.** `sdb` made every SDB chat look related.
  - **Prompt fence.** Text from other chats can no longer close the related-work fence.
  - **Emoji cuts.** Titles and outcomes could be cut inside an emoji.
  - **Ticket links.** The Chat page sent ticket links on every turn, so links the owner removed came back.
  - **Run limits.** All tickets shared one run limit in the ticket view.
  - **Wrong docs claim.** The docs said injection never shows an agent a card outside its grants. Corrected in D238.

## Left for later

- **Model-based extractor / LLM summary:** only the interface exists. It should run behind a queue, because extraction
  runs inline today.
- **pgvector:** not added. Designed as an extra `embedding` column with the same readable filter.
- **Dashboard token-savings tile:** the data is at `GET api/chat/cache` (the owner sees install totals). The Dashboard is
  being rebuilt on `phase1-cleanup-rbac`, so the tile belongs there.
- **Retention of index rows:** no TTL; rows outlive kagent's 14-day transcripts on purpose. A retention setting is an
  open question.
- **UI for sharing a session and for the ticket view:** API only. The Chat page has History, `?ticket=` and the
  related-work note.
- **Concurrent turns on one session:** last write wins on the derived row (no concurrency token). Two simultaneous
  first turns on one session id cannot happen through Chat (each creates its own kagent session).
- **Forgetting a session** drops its index row. If the person keeps chatting in that kagent conversation, the next turn
  indexes it again, titled by that turn.
- **Cache vs. access changes:** answers are cached per person and never built on other people's shared chats. A grant or
  team-membership change does not drop the person's own kept answers before the TTL (30 min, configurable). Those
  answers can only reflect what the agent read for that same person at the time.
- **Agent revision from kagent:** the cache keys on `metadata.generation` from kagent's `GET /api/agents`. Kubernetes
  objects carry it, but I did not check it against a live kagent (the local cluster had no session to try). If it is
  missing, agent edits do not drop cached answers; the TTL, model saves and card writes still do.
- **Cached answer, conversation expired:** a hit continues the original kagent conversation, and the page reopens its
  history. If kagent has expired that session, the page keeps the answer in view, and "Ask again without the cache"
  starts a fresh conversation. A follow-up typed without that would get "No such conversation".
- **Reload continuity test** checks the page wiring (sessionStorage save/restore, reopen, history call) and the API
  behind it. No JavaScript test runner is set up in this repo, so the browser behaviour was not driven by a test.
- **Chat history rendering after reload** reads kagent's session events in a tolerant way: ADK `content.parts`, A2A
  `parts`, thoughts skipped. The exact kagent 0.10 event shape was not checked against a live session (the local kagent
  had none). If nothing parses, the page says it is continuing the session and new messages still go to it.

## Test results

Final run, on the last code commit (`ea24093`):

- `dotnet build Skanyxx.sln -c Release`: **0 warnings, 0 errors**.
- `dotnet test --solution Skanyxx.sln -c Release`: **2258 passed, 0 failed, 0 skipped** (Host tests 27 min). An
  earlier full run on the first six code commits had 2248, all passing. The new tests are below.

| Project | New tests |
|---|---|
| `tests/Skanyxx.Module.ChatContext.Tests` (new, 52) | Access (9): no leak of a private chat via search, history, by-id, ticket view, related tickets or injection; personal hidden from supervisor/owner; team/department/company sharing; owner-only changes (404/403); anonymous 401. Index, search, ticket links, forgetting, emoji cuts. Extractor (keywords, weights, tickets vs standards, categories, outcome). Related work: same, written and linked ticket; company-only cards; budget; fence. Ticket view with runs and related tickets. Answer cache: hit/miss, per-person isolation, agent/ticket/fresh misses, card-write and model-save invalidation, owner-only clear-all, Redis down and timing out, not caching answers built on others' chats, stats. `ChatReader`. |
| `tests/Skanyxx.Host.Tests` (+5) | `ChatContextTests`: through the real Host and fake kagent, a new chat on a ticket gets the earlier chat and names it, later turns carry no context, another person gets nothing, the page's reload wiring, an invalid key is refused before kagent. `AnswerCacheTests`: real Redis in a container; a repeat question is answered without kagent, never for another person, and `noCache` asks again. |

The memory architecture test `OnlyTheHostAndMemory_UseLibraryUser` failed on my first design. I changed the design
(see above), not the test.

## Open questions

1. **Retention:** should index rows expire (e.g. 180 days), or follow the owner's transcript TTL?
2. **Injected context in kagent history:** kagent stores the first user message including the related-work block. The
   Chat page hides it on reload, but kagent-ui and exports will show it. Is that acceptable, or should it go as a
   separate system/context part when kagent supports one?
3. **Personal cards in injection:** I left them out so that nothing outside `LibraryUser`'s boundary reads a person's
   cards. The agent finds them itself over MCP. Do you want a memory-side contract that returns a person's own cards
   to the chat module (it would need a new rule in `LibraryUserTests`)?
4. **Cache scope:** answers are cached per person, which is the only scope guaranteed not to leak personal cards. A
   shared company-scope cache would need the agent to report that it used only company knowledge.
5. **Supervisor oversight of team-shared chats:** this mirrors cards (supervisors read team/department scopes). Personal
   chats stay private even from the owner. Confirm.
6. **Prompt injection through shared chats:** a teammate's shared session outcome is injected into your agent's first
   turn. It is fenced, labelled as data and stripped of brackets, but a model can still be swayed by it. Accept that, or
   inject only the reader's own sessions (a one-line rule in `RelatedAsync`; shared chats would stay searchable)?
7. **Ticket-key false positives:** the extractor skips UTF-8/SHA-256-style keys. Installs with Jira should set
   `ChatContext:TicketProjects` to their project keys.
