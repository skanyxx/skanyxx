# Review: memory-impl

Reviewed HEAD `a34d94b` against `phase1-cleanup-rbac`, the binding `docs/design/requirements.md`, the memory design notes, `MEMORY_REPORT.md`, and `MEMORY_GAPS.md` on 2026-10-27. Only this untracked report was written; tracked/source files were not changed. Build outputs and isolated test infrastructure were used for verification.

## BUILD

Command: `dotnet build Skanyxx.sln -c Release -p:UseSharedCompilation=false`

Exit code **0**. Complete diagnostic summary:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:08.98
```

## TESTS

Command: `dotnet test --solution Skanyxx.sln -c Release`

Exit code **0**. **2,237 passed, 0 failed, 0 skipped**. No primary-run failing assertions or exceptions were reported. Exact final summary:

```text
Test run summary: Passed!
  total: 2237
  failed: 0
  succeeded: 2237
  skipped: 0
  duration: 29m 59s 482ms
```

All five assemblies passed: Tickets (1m 02s 073ms), Memory (1m 08s 429ms), Host (29m 58s 731ms), Identity (1m 36s 850ms), and Sandboxes (55s 718ms). The long Host run completed normally; it was not cancelled. The passing suite does not cover the reproduced defects below.

Additional verification used F# fed through stdin to call the existing built Memory and Tickets harnesses against disposable Postgres containers. No probe source files were created. These probes reproduced C-1 through C-6; they are not additional xUnit test counts.

The two newly added Host tests also passed separately: **2 passed, 0 failed, 0 skipped** (9.231 s). A separate verbose Host diagnostic run was deliberately interrupted to stop duplicating the primary workload: 470 executed, 469 passed, 1 cancellation failure, 1 collection-cleanup error. Its cancellation failure was `FirstHourTests.Chat_NeverInjectsAnotherPersonsSessionCard`: `System.OperationCanceledException: The operation was canceled`, during `EntraSettingsCache.RefreshAsync` / Host startup (`FirstHourTests.cs:27`). Cleanup then reported `ObjectDisposedException: Cannot access a disposed object. Object name: 'IServiceProvider'` (`Infrastructure/HostApp.cs:168`). These were caused by the diagnostic run's SIGINT, not counted as primary-run or product failures; that same chat test passed in the separate two-test run.

Read-only live checks: `GET http://localhost:8083/health` and `GET http://localhost:8080/` both returned **200**. Controller `GET /api/agents` returned **200**, with `kagent/seed` labelled merged and `kagent/skanyxx-factory` unmerged; `GET /api/sessions` returned **200** with an empty list for the default caller. No live chat messages, A2A invocations, or kagent mutations were performed. The new memory behavior was checked with isolated fixtures, not by changing the live installation.

Jev recommended reviewing access control first (97% confidence) and classified the reproduced credential/data-loss risks as high priority (99–100% confidence). Findings below are supported by execution or direct source paths; severity reflects the stated impact.

## BUGS

### C-1 — HIGH — Automatic extraction exposes credentials in memory responses

- **Symptom:** A credential in a chat question or answer is copied verbatim into a persistent card and returned by the card API. It can subsequently enter related-memory prompts or be lifted with the card. This contradicts `requirements.md:42` (secrets must not appear in responses/UI; read-backs are set/not set).
- **Evidence:** `src/Modules/Skanyxx.Module.Memory/Sessions/CardExtractor.cs:43` copies the question into `What` and the answer into `Why`; `Sessions/SessionText.cs:24` and `:31` only collapse/cut text. An isolated turn containing synthetic API-key/password markers was followed by `GET /api/memory/cards/personal:ana/chat-synthetic-secret`: `SENSITIVE synthetic credential in card API=true`. No real credentials were used. `Sessions/RelatedMemory.cs:73` includes those fields in prompts.
- **Suggested fix:** Sanitize sensitive values before extracting/indexing cards, and enforce redaction at response/prompt boundaries. Cover question, answer, run outcome, history, and lifted cards with synthetic-secret regression checks.

### C-2 — HIGH — A rejected extraction deletes previously saved work

- **Symptom:** At the personal-card quota, a new extraction deletes an existing card before validating or accepting its replacement. If the replacement is rate-limited or fails, the old card and its cascading history remain deleted.
- **Evidence:** `src/Modules/Skanyxx.Module.Memory/Sessions/CardExtractor.cs:79` calls `MakeRoomAsync` before `mediator.Send`; `:98` executes an immediate delete. With `Memory:MaxCardsPerPersonalScope=1` and per-caller writes/minute=1, the fixture printed `first extraction kept=true`, `second extraction kept=false`, `remaining cards=0`. The second session was rejected by the write limiter after the first session's card was removed. History cascades on deletion (`Data/CardVersionConfiguration.cs:29`).
- **Suggested fix:** Validate and obtain write permission/rate capacity first. Make eviction and insertion one transaction under a scope lock; roll back eviction on every unsuccessful replacement and invalidate caches after commit.

### C-3 — HIGH — Concurrent chat completions silently lose accumulated ticket links

- **Symptom:** Two completions for one session can each succeed while the final card drops tickets/keywords contributed by one turn. Retrying with a fresh version replays stale metadata rather than merging it.
- **Evidence:** `src/Modules/Skanyxx.Module.Memory/Sessions/CardExtractor.cs:48` merges against the initial snapshot; `:84` retries with only `Version` changed. `Features/Cards/UpsertCardHandler.cs:115` replaces the arrays. Starting with `SDB-1`, then concurrently extracting turns mentioning `SDB-2` and `SDB-3`, lost one new ticket in **8/8** probes; e.g. `final tickets=[SDB-1,SDB-2]; expected SDB-1,SDB-2,SDB-3`. This contradicts D301's tickets-from-every-turn behavior.
- **Suggested fix:** On conflict, rebuild the update from the latest card and merge the new turn's metadata; preserve the original topic. Serialize per-session changes or use bounded merge retries that do not silently discard a turn.

### C-4 — MEDIUM — Scope quotas are bypassed by lifts and concurrent creates

- **Symptom:** `Memory:MaxCards*` does not bound stored cards. Lifts omit the quota check, and ordinary creates race between counting and inserting.
- **Evidence:** `src/Modules/Skanyxx.Module.Memory/Features/Cards/LiftCardHandler.cs:67` checks duplicate keys and inserts without checking target capacity. With `Memory:MaxCardsPerScope=1`, lifting a second card into a full company scope returned **201**, leaving **2** cards. `Features/Cards/UpsertCardHandler.cs:55` counts outside the insert transaction (`:84`); parallel creates with personal quota=1 both returned **201**, leaving 2 cards in **6/8** probes. D307 requires a full-scope create to be refused.
- **Suggested fix:** Centralize capacity enforcement for upsert, lift, and extraction, and serialize capacity checks plus insertion per scope in Postgres.

### C-5 — MEDIUM — Version history permanently omits replaced card details

- **Symptom:** Changing a card's body, source, links, keywords, agent attribution, or supersession pointer loses the old values. The history cannot show or reconstruct the replaced card, although D304 and `memory-card.md` promise every replaced version.
- **Evidence:** `src/Modules/Skanyxx.Module.Memory/Domain/CardVersion.cs:19` snapshots only summary fields, status, tickets, and time. The table in `Data/Migrations/20261027103213_CardLinksAndVersions.cs:76` lacks the other fields. A probe changed body/source/links/keywords successfully (**201**, then **200**); `GET .../history` returned version 1 with only `version,scope,key,type,what,why,who,status,tickets,updatedAt`. Neither old body nor old source pointer is retained in the version schema.
- **Suggested fix:** Persist a complete immutable card snapshot, including body/source or the old object-store pointer and all metadata. Add a migration and expose historical details under the existing authorization policy.

### C-6 — MEDIUM — Worker-internal failures never produce a run memory card

- **Symptom:** A run failed after repeated internal errors reaches a terminal state without notifying memory, so that failed work cannot be found later as a run card.
- **Evidence:** `src/Modules/Skanyxx.Module.Tickets/Engine/RunWorker.cs:137` calls `RunStepper.FailAsync` after three exceptions. `Engine/RunStepper.cs:70` directly updates the state to `Failed` and never publishes `TicketRunFinished`; publication was added only at `:61` and in the gate handler. An isolated Tickets fixture with the existing completion recorder registered printed `FAILED RUN state=Failed; TicketRunFinished notifications=0` after calling this method. `tests/Skanyxx.Module.Tickets.Tests/RobustnessTests.cs:15` exercises the worker's three-error path. `requirements.md:79` and D301 require finished runs to produce owner-scoped cards.
- **Suggested fix:** Publish the completion event for this terminal transition as well, with a fallback summary when the failing row cannot be fully loaded. Prefer a durable completion/outbox record so publication failures can be retried without rerunning stages.
