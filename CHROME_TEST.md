# Chrome test: phase1-cleanup-rbac (2026-10-28, round 2)

**Result: PASS** after five bugs were fixed. Every page loaded for the roles that should see it and was refused (403 page) for the rest. No secrets were rendered. Test totals are under [Tests](#tests).

Round 1 (earlier the same day) covered fewer panels, roles and flows. It found the access-denied loop, fixed as D236. This round adds every page and button for four roles, the kagent CRUD comparison, and fixes for the bugs it found.

## What was started

| What | How | Stopped after |
|---|---|---|
| Postgres | Throwaway container `crud-test-pg-phase1` (`postgres:16-alpine`) on `127.0.0.1:55491`, database `skanyxx`, recreated before each run | yes (`docker rm -f`) |
| Host | `dotnet build Skanyxx.sln -p:UseSharedCompilation=false`, then `dotnet run --no-build --project src/Skanyxx.Host --no-launch-profile` on `http://127.0.0.1:5391`. Development, env config as in `scripts/dev/first-hour.sh host`, `KAgent` = `localhost:8083`, tickets from the sample file. Script: `_scripts/host.sh` | yes |
| Ollama | `ollama serve` on `127.0.0.1:11434`, the `default-model-config` model `qwen3-coder:30b`. It was not running, so no agent could answer | yes |
| kagent | Already running: controller `localhost:8083`, UI `8080`. Not started, stopped or reconfigured. `seed` and `skanyxx-factory` were only read. Their resourceVersions were `706` / `795` before and after | n/a |

Not available, so not tested live:
- Studio git (Gitea `:3300`) was down, so Studio shows "not configured".
- Sandboxes are off by default.
- SMTP is unset, so `/ForgotPassword` answers 404 by design.
- Microsoft sign-in is not configured.

## How

Playwright, `channel: "chrome"` (`/Applications/Google Chrome.app`, headless), with throwaway scripts in `/tmp`. Copies are in `_scripts/` in the evidence folder: `walk.js`, `kcrud.js`, `extra.js`, the logs and `walk-results.json`.

1. **Owner.** `/Setup` creates `e2e-test-owner@example.com`. Then every page in turn: each tab, dialog and safe form, using only `e2e-test-*` / `crud-test-*` objects.
2. **Other roles.** The owner invites `e2e-test-employee`, `e2e-test-builder` and `e2e-test-supervisor`, and also revokes one invite. Each invite is accepted in Chrome.
3. **Role walk.** Each role opens every page by direct URL, then probes the admin APIs.
4. **Owner again.** Role change, password-reset link, disable and enable.
5. **Anonymous.** Every page and some APIs.
6. **Extra pass.** A password reset followed in Chrome, Plugins inspect validation, an Entra save, and the Sandboxes off state.

Each page was checked for:
- an HTTP status of 200, or 403 where the role should be refused
- no console errors
- no 4xx/5xx except expected ones (403 for refused roles; 405, 404, 409 and 400 from the script's own negative probes)
- no secret patterns in the DOM (`skx_mem_|inv_|rst_`, JWT, `sk-…`, `Bearer …`); the invite and reset links are allowed only on the owner's one-time display

**Screenshots:** `~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/evidence/chrome/phase1-cleanup-rbac/` holds 150+ PNGs, named `<role>-<page>.png` and `crud-*.png`.

## Pages visited

The routes come from `src/Skanyxx.Host/Pages/*.cshtml`, the sidebar in `_Layout.cshtml` and the module endpoints.

| Page | Owner | Employee | Builder | Supervisor | Anonymous | What it showed / what was clicked |
|---|---|---|---|---|---|---|
| `/` | PASS → /Chat | PASS → /Chat | PASS | PASS | → /Login | Redirect to Chat |
| `/Setup` | PASS | – | – | – | → /Login once an owner exists | Owner created, then → /Model |
| `/Login`, sign out | PASS | PASS | PASS | PASS | PASS | Sign in, sign out → /Login |
| `/Agents` | PASS | PASS | PASS | PASS | → /Login | Cards for `seed` and `skanyxx-factory`, plus kagent-made `crud-test-*` cards while they existed. Refresh works. **Details** opens a dialog (type, status, description, MCP tools, labels) and Escape closes it. **Chat** appears only on agents Chat offers. Write APIs answer 405 (read-only, D110) |
| `/Chat` | PASS | PASS | PASS | PASS | → /Login | The picker offers merged agents only. Analytics panel toggles; New Chat works. One message to `seed` got 502 "The agents cannot answer right now" (environment, see below). One message to the throwaway merged agent `crud-test-chat` got **200, reply "PONG" in 4 s** |
| `/Library` | PASS | PASS | PASS | PASS | → /Login | Personal card `e2e-test-card` created, then: search, filter, detail view, rename key → `e2e-test-card-renamed`, lift to `team:e2e-test-team` |
| `/ToolServers` | PASS | PASS (no add/delete) | PASS (no add/delete) | PASS (no add/delete) | → /Login | Add dialog: Cancel; invalid name blocked by the browser. `crud-test-ts` created ("Tool server created!"); a duplicate got 409 "already exists". Delete works. Other roles get 403 on the APIs |
| `/Account` | PASS | PASS | PASS | PASS | → /Login | Name, email, roles; "Microsoft sign-in is not enabled" |
| `/Studio` | PASS | 403 | PASS | PASS | → /Login | "needs the agent repository" (git off) |
| `/Plugins` | PASS | 403 | PASS | PASS | → /Login | Catalog, Git URL + commit and Upload zip tabs. A bad commit was blocked by the browser. An unreachable host got "crud-test.invalid is not github.com and not a git host the owner allowed" |
| `/Sandboxes` | PASS | 403 | 403 | PASS | → /Login | "Sandboxes are off" notice, no run form |
| `/Debug` | PASS | 403 | 403 | 403 | → /Login | Version, Host mode, health checks (kagent Healthy), modules |
| `/Model` | PASS | 403 | 403 | 403 | → /Login | Ollama / qwen3-coder:30b; the API key field is empty. An empty model id got a validation error and `default-model-config` was unchanged. **Not saved**, because seed uses this model |
| `/People` | PASS | 403 | 403 | 403 | → /Login | 4 invites, each link shown once and gone after reload; 1 revoked. Save roles, password-reset link, disable ×3, enable ×1 |
| `/Org` | PASS | 403 | 403 | 403 | → /Login | Department and team `e2e-test-*` created and renamed, member added. An invalid slug was blocked by the browser; a duplicate slug got 409 with a message |
| `/Entra` | PASS | 403 | 403 | 403 | → /Login | Saved disabled and empty ("Saved."); the client-secret field stays empty |
| `/Audit` | PASS | 403 | 403 | 403 | → /Login | Filter `invite.created`: 4 rows |
| `/Privacy`, `/Offline` | PASS | PASS | PASS | PASS | PASS | Static pages |
| `/Invite?token=…` | – | PASS | PASS | PASS | 404 without a token | Account created, then → /Chat |
| `/ResetPassword?token=…` | – | – | – | PASS | 404 without a token | `Referrer-Policy: strict-origin`. New password set, then → /Login?reset=true; signing in with it works; reusing the link gets 404 |
| `/ForgotPassword` | – | – | – | – | 404 | By design: only when SMTP is configured |
| Sidebar | all 14 entries | Agents, Chat, Library, Tool Servers | + Studio, Plugins | + Sandboxes | none | Matches the page gates |
| Admin APIs, as a role without access | – | 403 | 403 (Studio and catalog: 200) | 403 (Studio, catalog, agent-secret *status*: 200) | 401 | `/api/memory/agents/{id}/secret` GET returns only whether a secret exists and since when, never the value |

**Chat with `seed` (environment, not a Skanyxx bug):** `seed`'s memory MCP (`RemoteMCPServer skanyxx-memory-seed`) points at `host.docker.internal:5287/mcp/memory`, which is another worktree's Host. That Host refused the MCP `initialize`, so kagent failed the task. Skanyxx answered 502 with a fixed text and leaked nothing. `seed` was not changed. A real chat turn was shown instead with a throwaway merged agent that has no tools.

## Bugs fixed

The first four came from the memory-impl branch's Chrome fixes (`f8b4a6b`), ported here without its access-denied part, which is D236 on this branch. Their decision rows are renumbered for this branch. The fifth is new.

1. **Nobody could accept an invite or finish a password reset in Chrome (403)** (D237).
   - **Cause:** under `Referrer-Policy: no-referrer`, Chrome posts the page's own form with `Origin: null`, and the origin guard refused it.
   - **Fix:** the Invite and ResetPassword pages answer `strict-origin`. The test `Browser` now sends the `Origin` a real browser sends, so every form test goes through the guard.
   - **Seen in Chrome:** `403 POST /Invite?token=…` before; accept → /Chat after.
2. **Tool server create always failed** (D238).
   - **Cause:** kagent answered "Invalid ToolServer metadata: empty reference string". Skanyxx sent `{remoteMCPServer: {name, endpoint}}`, not kagent's `{metadata, spec: {url, protocol}}`.
   - **Fix:** `POST /api/toolservers {name, namespace, url}` now builds kagent's shape. It validates the name, namespace and URL, refuses credentials in the URL, answers 409 for a duplicate, and gives fixed texts for kagent errors. The page sends the namespace and escapes kagent's text.
   - **Tests:** `ToolServersAndAgentsApiTests`.
3. **Agents → Details went to `/Debug?agent=…`** (D238).
   - **Cause:** this branch made `/Debug` owner-only diagnostics that ignore `agent`. The owner saw Debug; everyone else got a 403.
   - **Fix:** `GET /api/agents/{namespace}/{name}` plus a Details dialog. Card text is now escaped (it was raw `innerHTML`).
4. **`pattern` attributes were silently ignored by Chrome** (D239).
   - **Cause:** Chrome compiles `pattern` with the `v` flag, where `[a-z0-9-]` is a syntax error ("Pattern attribute value … is not a valid regular expression"). Bad slugs and names reached the server.
   - **Fix:** escape `-` (Org, Studio, Tool Servers).
   - **Test:** `PagePatternTests` compiles every pattern under `v`.
5. **The Chat button sent the message to the wrong agent** (D240, new).
   - **Cause:** the Agents page showed Chat on every card. For an agent Chat does not offer (`skanyxx-factory`, or any unmerged agent such as a kagent-made `crud-test-agent-k`), the Chat page fell back to `seed`, so the message went to an agent the person had not picked.
   - **Fix:** `GET /api/agents` carries `inChat` (the merged label, D100), and the page shows Chat only then.
   - **Regression test:** `ToolServersAndAgentsApiTests.AgentsList_SaysWhichAgentsChatOffers`.
   - **Re-checked in Chrome:** no Chat on the factory or `crud-test-agent-k`; Chat on `seed` and on `crud-test-chat`.

Every fix was rebuilt and checked again in Chrome; the tables above are from the final run.

## kagent CRUD comparison

Throwaway objects only: `crud-test-agent-k`, `crud-test-chat`, `crud-test-model`, `crud-test-ts-kagent`, `crud-test-ts-skx` and `crud-test-agent` (a memory secret). After the run, `kubectl get agents,modelconfigs,remotemcpservers,secrets | grep crud-test` returned nothing. The kagent sessions created by the test were deleted.

| Surface | Operation | kagent | Skanyxx | Same / different | Note |
|---|---|---|---|---|---|
| Agents | create | UI form: name, namespace, type, sandbox, ADK runtime, description, instructions (default "You're a helpful agent, made by the kagent team…"), model, stream (off), service account, skills (OCI, git, S3), memory/embedding, compaction | None on the Agents page; `POST /api/agents` → 405. Agents are made through the Studio as pull requests | different, **by design** | D110: Studio-managed agents. The Studio needs git, which was down, so a Studio create was not exercised |
| Agents | create validation | empty → "Agent name is required / Description is required / Please select a model"; `Bad_Name` → RFC 1123 text; duplicate → "Failed to create Agent in Kubernetes" (generic) | n/a | different | kagent's duplicate message gives no reason |
| Agents | stored spec | kagent UI create → `{type: Declarative, description, declarative: {runtime: go, systemMessage, modelConfig}}`, no labels | n/a | – | – |
| Agents | read / list | list with model, status, menu | Cards: name, namespace, type, Ready badge, description. Details dialog: type, accepted/ready, description, MCP tools, labels. The kagent UI edit showed up in Skanyxx on refresh | same data, smaller view | Skanyxx does not show the system prompt or model. Unknown agent → 404 |
| Agents | update | Edit page; changes only the description, rest of spec unchanged | `PUT` → 405 | different, **by design** | D110. The owner's open decision on Studio-agent edits was left alone |
| Agents | delete | Menu → "Delete Agent. Are you sure…?" → gone (controller 404) | `DELETE` → 405; the card disappears after a kagent delete | different, **by design** | D110 |
| Agents | protected system agents | can edit or delete `seed` and the factory | read-only, Details only; no Chat on the factory | different, **by design** | Never touched; resourceVersions `706` / `795` unchanged |
| Chat / sessions | create | kagent UI chat per agent, sessions of the kagent UI user ("No chats yet" for our agent) | Chat only with merged agents. `crud-test-chat` answered "PONG" (200, 4 s); the session belongs to the Skanyxx person's id (`user_id`) | different, **by design** | D100 / D017: Chat lives in Skanyxx. Skanyxx sessions are not visible in the kagent UI under its admin user |
| Chat / sessions | delete | kagent API `DELETE /api/sessions/{id}?user_id=` → 200 | No session list or delete in the Skanyxx UI ("New Chat" only) | **missing feature** | Sessions are personal (requirements O1). There is no way to delete a past chat in Skanyxx |
| Model configs | create | `/models/new`: 10 providers, 28 OpenAI models, generated name, API key → kagent creates Secret `crud-test-model`; spec `{model, provider, apiKeySecret, apiKeySecretKey: OPENAI_API_KEY, openAI}` | No create: `/Model` edits only `default-model-config` (providers OpenAI, Anthropic, Ollama) | different, **by design** | One model per install today |
| Model configs | read | list with details | `/Model` shows provider, model and base URL; the API key is never shown | same data; key hidden **by design** | – |
| Model configs | update | edit: provider/model locked, key rotated (Secret resourceVersion changed) | Server validation: empty model → "'model' must not be empty…"; nothing sent to kagent (`default-model-config` resourceVersion unchanged). A valid save was **not** run, because seed uses this model | similar | – |
| Model configs | delete | row → Delete → confirm; the Secret is removed too | none | **missing feature** | Only one model, so nothing to delete |
| Tool servers (MCP) | create | `/mcp/new` defaults: protocol **SSE**, timeout **5s**, sseRead 300s, terminateOnClose. Bad name + URL → "Please enter a valid URL…", "Name must conform to RFC 1123…" | Dialog: remote (streamable HTTP) only, namespace `kagent`. Bad name and URL are blocked by the browser; the API refuses `ftp://` ("must be an absolute http or https address") and a duplicate gets 409. Stored as protocol **STREAMABLE_HTTP**, timeout **30s** (kagent's CRD default) | different defaults | Skanyxx offers no stdio MCPServer type, and no timeout, header or description fields |
| Tool servers (MCP) | read / list | `/mcp`: 3 servers, tool counts, an undiscovered server shows 0 tools | Same 3 refs; badge Online / No Tools; first 5 tools | same | – |
| Tool servers (MCP) | update | no edit control (only "Remove server") | none | same | Neither has an edit |
| Tool servers (MCP) | delete | menu → Remove server → gone | owner's × → confirm → gone; a second delete gets 404 "No such tool server." | same | Add and delete are owner-only in Skanyxx (D116); other roles get 403 |
| Secrets / keys | create, read, delete | no secrets UI or API; Secrets appear only as a side effect (model API key) | Agent memory secret: `POST` returns it **once**, `GET` returns status only, `DELETE` 204 then `hasSecret: false` | different, **by design** | Secrets are never shown again, and never in the UI |
| Memory / knowledge | all | `GET /api/memories?agent_name=seed` → 500 `relation "memory" does not exist` (kagent memory is not enabled) | Library cards: create (API), search, detail, rename, lift | different, **by design** | Company knowledge is Skanyxx cards, not kagent TTL memory (AGENTS.md). kagent's own error is a kagent install detail |
| People, org, roles, audit | all | none | full CRUD in Skanyxx | Skanyxx only | – |

## Tests

`dotnet test Skanyxx.sln` (after all fixes, Docker on OrbStack): **2260 passed, 0 failed, 0 skipped** (2260 total). An earlier full run, before the QA fixes and their five new test cases, had 2255/2255.

## QA

An independent code review of the diff returned **PASS** (no must-fix). The full text is in the task dir under `review/code-reviewer-phase1-cleanup-rbac-2.md`.

Applied:
- **Tool server URLs:** a query parameter named like a key, token, secret, password or auth is refused (the spec is plain text in the cluster). New test cases are in the invalid-input theory, and the refusal was seen in Chrome.
- **Owner-only tool servers (D116):** API tests show that an employee, builder or supervisor gets 403 on create and delete, and nothing reaches kagent.
- **Agents Details:** a slow answer for an agent closed meanwhile no longer fills the next agent's dialog (checked in Chrome).

Left as follow-ups:
- kagent's own 4xx → 400 branch is not tested.
- Two creates racing on the same name answer 502, not 409.
- The Details dialog has no focus trap.
- An anonymous → 401 test for `GET /api/agents/{ns}/{name}` (it relies on the fallback policy).
- Skanyxx has no way to delete a past chat session (see the comparison table).
