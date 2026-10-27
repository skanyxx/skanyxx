# FIX_REPORT — Codex review round 1 (agents-crud)

Source: `CODEX_ISSUES.md` (C-1…C-8). Fixed in order HIGH → MEDIUM → LOW, one commit per ID. Paths under `Manage/` are
in `src/Modules/Skanyxx.Module.Agents/`. Tests are in `tests/Skanyxx.Host.Tests/`. Decision: D257 (`docs/design/decisions.md`).

| ID | Sev | Status | What changed | Test (fails without the fix) | Commit |
|---|---|---|---|---|---|
| C-1 | High | **Fixed** (fail closed) | An agent that names one MCP server (or agent) in several tool entries is never edited here: `AgentSpec.HasUneditableTools` (also other-namespace/other-kind tools) → 409 before any write, `canEdit: false`. Each entry may carry its own Secret header, and the form names each server once, so merging them would move a credential. Files: `Manage/Definition/AgentSpec.cs`, `Manage/Features/UpdateAgentHandler.cs`, `Manage/Features/AgentFailure.cs`. Editing such agents entry-by-entry would need a per-entry form (not built). | `AgentsApiTests.AnAgentNamingOneServerTwice_IsNeverEdited_SoNoCredentialMoves` (probe: `search`/token-a + `fetch`/token-b, save as one entry) | `854f5b3` |
| C-2 | High | **Partially fixed** | The detail carries `version` (SHA-256 of the spec kagent holds); `PUT` must send it: missing → 400, stale → 409, nothing written. The page sends it. Files: `Manage/Definition/AgentForm.cs`, `AgentDetail.cs`, `AgentSpec.cs`, `Manage/Features/UpdateAgentHandler.cs`, `CreateAgentHandler.cs`, `Pages/Agents.cshtml`. **Left:** kagent 0.10.2's PUT takes no precondition (it does its own GET then Update and ignores the body's resourceVersion), so a write landing in the milliseconds between Skanyxx's read and kagent's update can still be lost. Closing that needs an upstream kagent change (honour `metadata.resourceVersion` on PUT). | `AgentsApiTests.AStaleEdit_IsAConflict_AndNeverOverwritesTheNewerChange` (probe: open form, another writer changes the prompt, stale save) | `a0b7674` |
| C-3 | High | **Fixed** | Every create/update/delete (and suspend/resume) writes `agent.<verb>_requested` **before** the controller call; if that row cannot be written, the request fails and kagent is never called (fail closed). The outcome (`agent.created/updated/deleted`, or `agent.write_failed` when kagent refuses) is written after; a failing outcome row is logged at Error and never turns a change kagent applied into an error — the intent row stands. Files: `Manage/Features/AgentAudit.cs`, `CreateAgentHandler.cs`, `UpdateAgentHandler.cs`, `DeleteAgentHandler.cs`; `Infrastructure/FakeKAgent.cs` (`FailAgentWrites`). | `AgentsAuditTests` (new): `WithoutTheIntentRow_NothingReachesKAgent`, `AnAppliedWrite_IsAnsweredAsDone_EvenWhenItsOutcomeRowFails_AndItsIntentRowStands`, `AWriteKAgentRefuses_IsAuditedAsFailed` (audit handler replaced by one that fails on chosen actions) | `9a4c14f` |
| C-4 | High | **Fixed** | Condition messages are never returned (free text from kagent/Kubernetes); type and reason only when they are a CamelCase token (`^[A-Za-z][A-Za-z0-9]{0,63}$`), status only True/False/Unknown. The page no longer renders a message. Files: `Manage/Definition/AgentCondition.cs`, `AgentSpec.cs`, `Pages/Agents.cshtml`. | `AgentsApiTests.ConditionMessages_AndOddReasons_NeverReachTheResponse` (bearer marker in message and reason) | `b36749f` |
| C-5 | Medium | **Fixed** | A form A2A skill is applied over the agent's skill with the same id, so `inputModes`/`outputModes` (and anything else the form does not show) stay. File: `Manage/Definition/AgentSpec.cs`. | `AgentsApiTests.AnEdit_KeepsTheA2aSkillsMimeModes` (probe skill `describe`, image/png → application/json) | `073b0c1` |
| C-6 | Medium | **Fixed** | The Chat button passes the encoded `namespace/name`, which Chat matches exactly. File: `Pages/Agents.cshtml`. | `AgentsApiTests.TheChatButton_NamesTheAgentsNamespace` (served page carries the namespaced link, not the bare-name one) | `b801ab3` |
| C-7 | Medium | **Fixed** | `POST /api/agents/{ns}/{name}/suspend` / `…/resume` (owner, supervisor). Suspend sets `deployment.replicas: 0` (kagent 0.10.2 has no suspend field; it passes replicas to the Deployment — checked in its translator), resume removes the override (kagent default: 1 replica). Chat's `MergedAgents` skips agents scaled to zero for listing and sending. Same gates as edit (namespace, never studio/system agents — studio agents keep the owner's Studio suspend, D121), intent/outcome audit, idempotent. Page: Suspend/Resume buttons, "Suspended" status. D257 + roles.md row. Files: `Manage/Features/SetAgentSuspended{Command,Handler}.cs`, `Manage/Endpoints/{Suspend,Resume}AgentEndpoint.cs`, `AgentSpec.cs`, `AgentDetail.cs`, `AgentSummary.cs`, `ListAgentsHandler.cs`, `AgentAudit.cs`, `src/Skanyxx.Core/Models/Agent.cs`, `src/Skanyxx.Core/Services/KAgentApiClient.cs`, `src/Modules/Skanyxx.Module.Chat/Features/MergedAgents.cs`, `Pages/Agents.cshtml`. **Note:** resume restores one replica, not a previous custom count (kagent's PUT cannot keep it in metadata). | `AgentsApiTests.Supervisors_SuspendAndResume_AndChatNeverOffersASuspendedAgent`, `SystemAndStudioAgents_AreNeverSuspendedHere`; RBAC test now covers both routes (builder/employee 403, anonymous 401) | `1f4a6b4` |
| C-8 | Low | **Fixed** | After kagent refuses a create, Skanyxx checks whether the name exists now; if so 409 `exists` (audited `write_failed` "name taken"), never a retry. File: `Manage/Features/CreateAgentHandler.cs`; `FakeKAgent.AgentCreatedMeanwhile`. | `AgentsApiTests.ACreate_LosingARaceForItsName_IsAConflict` | `88e5fe5` |

Studio-managed agent edits are unchanged (still blocked, D252), as instructed.

## Live check (kagent controller localhost:8083, throwaway Host + Postgres, removed afterwards)

`crud-test-173250`: **32/32** — includes update without version 400, stale version 409, suspend (controller
`replicas: 0`, gone from employee Chat), resume (override gone, back in Chat), owner suspend of `seed` 409, the full
intent/outcome audit trail, and `seed`/`skanyxx-factory` resourceVersion unchanged (706/795). kagent afterwards lists
only `seed` and `skanyxx-factory`.

## Build and tests

- `dotnet build Skanyxx.sln -c Release -p:UseSharedCompilation=false`: **0 errors, 0 warnings** (after C-8, the build the
  final test run used).
- Agents area during the fixes: `AgentsApiTests` + `AgentsAuditTests` 26/26 after C-8.

## Final run

`dotnet test --solution Skanyxx.sln -c Release` (on the C-8 build): **2226 total, 2226 passed, 0 failed, 0 skipped**
(Host 29 min; Identity, Memory, Sandboxes, Tickets passed). Was 2215 before: +11 regression tests (8 in
`AgentsApiTests`, 3 in the new `AgentsAuditTests`).

Uncommitted files after the commit of this report: **0 tracked changes**. Two files are untracked and left alone on
purpose: `CODEX_ISSUES.md` and `docs/design/requirements.md` (not to be modified; not mine to commit).

## Left / blocked

- **C-2 residual** (upstream): the sub-second window inside kagent's PUT; needs kagent to honour a resourceVersion
  precondition. Recorded in `ISSUES.md` O1.
- **C-1 broader:** agents with repeated tool entries can only be edited where they were made; a per-entry form would
  lift that.
- **C-7 note:** resume returns to kagent's default replica count.
