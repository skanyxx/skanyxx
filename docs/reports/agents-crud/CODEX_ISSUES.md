# Debugging review — agents-crud

Reviewed `b57ed75..e715dbd` against `phase1-cleanup-rbac`, the binding `docs/design/requirements.md`, the design decisions, `AGENTS_CRUD_REPORT.md`, and `ISSUES.md`.

Final verification: no tracked-file changes or staged changes. The only new non-build output is this untracked report; the pre-existing untracked `docs/design/requirements.md` was preserved.

## BUILD

Command: `dotnet build Skanyxx.sln -c Release -p:UseSharedCompilation=false`

Result: **PASS**, exit 0; **0 errors, 0 warnings**. Elapsed: 7.09 seconds.

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## TESTS

Command: `dotnet test --solution Skanyxx.sln -c Release`

Result: **PASS**, exit 0; **2,215 passed, 0 failed, 0 skipped**. Total duration: 29m 45.396s. There are no failing tests or failure assertions/exceptions to list.

| Test assembly | Result | Duration |
|---|---|---|
| Identity | Passed | 1m 28.584s |
| Sandboxes | Passed | 56.006s |
| Memory | Passed | 54.273s |
| Host | Passed | 29m 44.684s |
| Tickets | Passed | 1m 00.610s |

```text
Test run summary: Passed!
  total: 2215
  failed: 0
  succeeded: 2215
  skipped: 0
  duration: 29m 45s 396ms
```

## BUGS

Findings below are from the branch diff. Additional probes invoked the compiled Release code with in-memory JSON/HTTP/audit fakes, or evaluated the page's JavaScript expressions. Probe source was supplied through stdin; only a DLL under test `bin/` was generated. These probes are separate from the solution test totals.

Read-only live checks: controller `GET /api/agents`, `GET /api/agents/kagent/seed`, `GET /api/agents/kagent/skanyxx-factory`, `GET /api/modelconfigs`, and `GET /api/toolservers` returned 200; UI `GET http://localhost:8080/` returned 200. The controller's detail envelopes contain `data.agent`, matching the client. The compiled list/detail handlers were also exercised against the live controller with fixture role principals: owner saw both protected system agents; employee saw only seed, without its prompt, and received NotFound for factory. This handler check is separate from the authenticated HTTP tests in the solution suite. Seed/factory resourceVersions were `706`/`795`. No live mutation was attempted. The live install has only these two agents, so edge cases below were reproduced in memory.

After the test run, the live controller still listed exactly seed and factory, with resourceVersions unchanged at `706`/`795`.

Jev severity triage: High findings, 58–94% confidence; Medium findings, 58–91%; Low finding, 35%. Severity reflects the demonstrated impact, not evidence of a production incident.

### C-1 — High — Saving duplicate MCP-server entries silently switches credentials

- **Symptom:** An existing agent has two entries for server `docs`: `search` uses Secret reference `token-a`, and `fetch` uses `token-b`. Opening and saving the form collapses them into `fetch` authenticated with `token-a`.
- **Evidence:** `src/Skanyxx.Host/Pages/Agents.cshtml:284` collapses entries with `Map`; `Manage/Definition/AgentSpec.cs:54` retains the last entry's tool set, but `AgentSpec.cs:88` clones the first matching entry's headers. Compiled-code probe matching that UI projection returned `validation errors=0` and a single entry with `toolNames:["fetch"]`, `headersFrom.valueFrom.name:"token-a"`. Paths under `Manage/` are relative to `src/Modules/Skanyxx.Module.Agents/`. The [kagent Tool schema](https://github.com/kagent-dev/kagent/blob/v0.10.2/go/api/v1alpha2/agent_types.go) permits a header configuration per entry.
- **Suggested fix:** Preserve individual tool-entry identity throughout the form and mapper. Until that is supported, refuse editing agents with repeated MCP-server references before any write.

### C-2 — High — A stale edit overwrites newer changes without a conflict

- **Symptom:** A supervisor opens a form; another writer changes the prompt; the first supervisor changes only the description and saves. The old browser prompt replaces the newer prompt successfully.
- **Evidence:** `Manage/Definition/AgentDetail.cs:10` and `AgentForm.cs:10` carry no concurrency token. `Manage/Features/UpdateAgentHandler.cs:29` reads the current object, then lines 51–52 apply every stale form field and PUT the spec. Compiled-handler probe: `Stale edit outcome=Ok; stored prompt=stale browser prompt`, replacing `concurrent supervisor prompt`. Already acknowledged in `ISSUES.md` O1. [kagent's update handler](https://github.com/kagent-dev/kagent/blob/v0.10.2/go/core/internal/httpserver/handlers/agents.go) replaces the spec after its own GET, ignoring the submitted metadata version.
- **Suggested fix:** Round-trip an edit version/hash and return 409 for stale forms; require an upstream atomic version precondition for the final write. A process lock alone cannot protect stale browser forms or external kagent writers.

### C-3 — High — Successful controller writes can remain unaudited and return an error

- **Symptom:** If the audit insert fails after a controller mutation succeeds, the request fails even though the agent changed, and no durable audit record exists.
- **Evidence:** `Manage/Features/CreateAgentHandler.cs:31`, `UpdateAgentHandler.cs:52`, and `DeleteAgentHandler.cs:35` mutate first and audit afterward. `src/Modules/Skanyxx.Module.Identity/Features/Audit/RecordAuditHandler.cs:10` directly awaits the insert. Compiled delete-handler probe with a failing audit dependency: `controller delete applied=True; handler exception=InvalidOperationException: synthetic audit store outage`. This violates `requirements.md:86` (every write audited).
- **Suggested fix:** Persist a durable audit intent before calling the controller, fail closed if that cannot be stored, and reconcile/finalize the outcome after the external operation.

### C-4 — High — Agent condition diagnostics expose credential text without redaction

- **Symptom:** Credential-bearing upstream condition text is returned unchanged to owners, supervisors, and builders, then displayed in the details panel.
- **Evidence:** `Manage/Definition/AgentSpec.cs:166` copies condition reason/message directly, truncating messages only; `src/Skanyxx.Host/Pages/Agents.cshtml:195` renders them. A compiled-mapper fixture containing `Authorization: Bearer SYNTHETIC-SECRET-MARKER` returned `Condition secret marker returned=True`. This is a synthetic credential test, not a claim that the current live agents expose a real credential. `requirements.md` §1.4 requires secret-free responses/UI and §3.12 requires redacted diagnostics.
- **Suggested fix:** Redact or omit untrusted diagnostic text before serialization, including condition reasons, and cover bearer/key/password markers in response tests.

### C-5 — Medium — A description-only save removes A2A MIME restrictions

- **Symptom:** Existing A2A skills lose their `inputModes` and `outputModes`, changing the advertised capability contract even when only the agent description is edited.
- **Evidence:** `Manage/Definition/AgentSpec.cs:71` rebuilds `a2aConfig`; lines 123–129 and 164–165 omit both fields. Compiled round-trip output changed `{"id":"describe","name":"Describe image","inputModes":["image/png"],"outputModes":["application/json"]}` to `{"id":"describe","name":"Describe image"}`. Both fields belong to the [kagent AgentSkill schema](https://github.com/kagent-dev/kagent/blob/v0.10.2/go/api/v1alpha2/agent_types.go). This contradicts D251's preservation rule.
- **Suggested fix:** Clone matching existing skills and update only form-owned fields, or expose and round-trip both MIME fields.

### C-6 — Medium — Chat buttons can open the agent in the wrong namespace

- **Symptom:** Clicking Chat for `b/helper` selects `a/helper` when both names exist and `a/helper` is listed first.
- **Evidence:** `src/Skanyxx.Host/Pages/Agents.cshtml:160` puts only `a.name` in the link. `src/Skanyxx.Host/Pages/Chat.cshtml:877` falls back to the first matching bare name. Executing those JavaScript expressions produced `Clicked b/helper; URL /Chat?agent=helper; Chat selects a/helper`.
- **Suggested fix:** Pass the encoded full `namespace/name` reference; Chat already supports exact references.

### C-7 — Medium — Required enable/suspend operations are absent

- **Symptom:** Maintainers/admins cannot enable or suspend a direct agent through this slice's UI/API.
- **Evidence:** `docs/design/requirements.md:83` explicitly requires enable/suspend. `Manage/Endpoints/` provides list/details/options/create/update/delete only; `AgentForm.cs` has no lifecycle state and `src/Skanyxx.Host/Pages/Agents.cshtml` has no lifecycle action. `ISSUES.md` O5 explicitly confirms the omission.
- **Suggested fix:** Add audited owner/supervisor enable/suspend actions through the controller and make Chat honor suspension.

### C-8 — Low — Concurrent name collisions return 502 instead of 409

- **Symptom:** If another create wins after the list precheck, the losing request reports controller unavailability instead of an existing-name conflict.
- **Evidence:** `Manage/Features/CreateAgentHandler.cs:22` and line 27 precheck the list, but lines 36–39 map every controller HTTP exception to `Unavailable`. Compiled-handler probe: `Racing create AlreadyExists outcome=Unavailable`; `src/Skanyxx.Core/Platform/OutcomeResults.cs:18` maps that to HTTP 502. [kagent's create handler](https://github.com/kagent-dev/kagent/blob/v0.10.2/go/core/internal/httpserver/handlers/agents.go) wraps Kubernetes create errors as 500. Already acknowledged in `ISSUES.md` O2.
- **Suggested fix:** Preserve a structured upstream error, or verify the collision after failure, and return 409 without retrying the create.
