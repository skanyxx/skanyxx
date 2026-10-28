# Fix report — Codex review of `memory-impl` (2026-10-27)

Source: `CODEX_ISSUES.md` (6 findings: 3 HIGH, 3 MEDIUM, 0 LOW). Fixed in order HIGH → MEDIUM, one commit each.
Decision rows: D309–D311 (`docs/design/decisions.md`, commit `8348f53`).

| ID | Sev | Status | What changed | Regression test | Commit |
|---|---|---|---|---|---|
| C-1 credentials copied into cards | HIGH | **fixed** | New `Memory/Domain/SecretText.cs` (`Redact`, `HasCredential`). `Sessions/CardExtractor.cs` redacts question, answer, run title and outcome before cutting/indexing, and drops a ticket URL with credentials. `Sessions/RelatedMemory.cs` redacts the prompt lines again. `Features/Cards/UpsertCardValidator.cs` refuses what/why/body/source/links that carry a credential. `Sessions/SessionText.cs`: "redacted" is not a keyword. | `CardSecretTests` (synthetic secrets in a chat turn are absent from card API, history, lifted copy, search, prompt; run outcome redacted; REST and MCP writes refused; redaction shapes; prose untouched) | `35cba3b` |
| C-2 rejected extraction deletes saved work | HIGH | **fixed** | New `Features/Cards/ScopeCapacity.cs`: per-scope advisory lock + quota count + (extraction only) eviction, all inside the create's transaction and after the rate limiter. `UpsertCardHandler` create is transactional (rollback before the conflict read). `CardExtractor` no longer evicts on its own (`UpsertCardCommand.EvictExtracted`). Lock key listed in `MemoryMigrator`. | `ExtractionTests.ARefusedExtraction_AtTheQuota_DeletesNothing` (quota 1, 1 write/min: the first card survives) | `447ab85` |
| C-3 concurrent turns lose tickets | HIGH | **fixed** | `CardExtractor.WriteAsync` re-reads the card and rebuilds the merge on every attempt (≤ 5), keeping the topic. | `ExtractionTests.ConcurrentTurnsOfOneSession_KeepEveryTurnsTickets` (8 rounds; confirmed failing on the old extractor: `[SDB-1, SDB-2]`), `ConcurrentFirstTurns_MakeOneCard_WithBothTickets` | `f33f8b0` |
| C-4 quotas bypassed by lift / racing creates | MED | **fixed** | `LiftCardHandler` inserts inside a transaction behind `ScopeCapacity` (403 when full). The create race is closed by the C-2 lock. | `ScopeQuotaTests` (lift into full company → 403, one company card; 4 parallel creates into the last slot × 8 rounds → exactly one 201, one row) | `7539f2b` (lock from `447ab85`) |
| C-5 history drops replaced details | MED | **fixed** | `CardVersion` + `CardVersionConfiguration` keep body, source, links, keywords, origin, agent_id, superseded_by; migration `20261027153145_CardVersionSnapshots`; `CardVersionDto` exposes them under the card's read rules. | `WriteRulesTests.History_KeepsTheWholeReplacedCard` | `6d42047` |
| C-6 worker-failed runs leave no card | MED | **fixed** (no outbox) | `Tickets/Engine/RunStepper.FailAsync` publishes `TicketRunFinished` when it ended the run: from the full run, or from its plain columns with the reason as outcome if the row cannot load. `RobustnessTests.ExplodingOn` made internal for reuse. | `RunFinishedTests.ARunTheWorkerFailsOnInternalErrors_IsPublishedAsFailed` (confirmed failing without the fix: no notification) | `c6e9e9e` |

## Results

- Build: `dotnet build Skanyxx.sln -c Release -p:UseSharedCompilation=false` → **0 warnings, 0 errors**.
- Tests: `dotnet test --solution Skanyxx.sln -c Release` → **2257 passed, 0 failed, 0 skipped** (Host, Identity,
  Memory 434, Sandboxes, Tickets 314). Before these fixes: 2237.
- Uncommitted files after the FIX_REPORT commit: **2**, both untracked and deliberately not touched —
  `CODEX_ISSUES.md` and `docs/design/requirements.md`.
- Nothing pushed. No other worktree touched. No live kagent mutation was needed for these fixes.

## Left / not done

- **C-1:** cards stored *before* this fix are not rewritten (the prompt block still redacts them). The full-body
  endpoint (`GET …/body`) streams stored bodies as written; new bodies with credentials are refused at write.
  Redaction is pattern-based: a credential in a shape it does not know can still get through.
- **C-6:** publication is still best-effort, done after the state change. If memory is unreachable when a run ends,
  that run's card is missing, and nothing retries it. A durable outbox (as Codex suggested) would be a separate slice.
- **C-5:** versions recorded before the migration show empty links/keywords and no body/source (never made up).
