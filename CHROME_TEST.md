# Chrome test — branch `agents-crud` (2026-10-28)

Real Google Chrome (`/Applications/Google Chrome.app`) via Playwright `channel: "chrome"` (headless), scripts in a temp
dir outside the repo (copies: `evidence/chrome/agents-crud/_scripts/`).

**Screenshots (150+):** `~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/evidence/chrome/agents-crud/`
(`<role>-<page>.png`, `crud-kagent-*` / `crud-skanyxx-*` for the comparison, `recheck-*` after the fixes).

## What was started (and stopped afterwards)

| Piece | How | After |
|---|---|---|
| Postgres | throwaway `postgres:16-alpine` container `skx-agents-e2e-pg` on `127.0.0.1:55511` (compose's image/credentials) | removed |
| Host | `dotnet build Skanyxx.sln -p:UseSharedCompilation=false`, then the `first-hour.sh host` env (Development, `127.0.0.1:5393`, `KAgent` at `localhost:8083`, 300 s chat turn, sample tickets, connection strings by env) | stopped |
| Ollama | `ollama serve` (was down; the default ModelConfig is Ollama `qwen3-coder:30b`) | stopped |
| kagent | existing controller `:8083`, UI `:8080`, port-forwards | untouched |

`/Setup` created the owner (`e2e-test-owner@example.com`); the owner invited `e2e-test-employee`, `-builder`,
`-supervisor`, each accepted in Chrome. The first run was discarded (two copies of the script ran at once); everything
below is from the clean run on a fresh database.

## Walkthrough (every page in `Pages/*.cshtml` and the nav)

Checked on each: HTTP status / final URL, console errors, 4xx/5xx in network (expected 403/404/409 listed), no secrets
in the DOM (`skx_*` tokens, JWTs, `sk-…`, `Bearer …`; the one-time invite/reset links on People are intended).

### Owner

| Page | Result | What it showed / did |
|---|---|---|
| `/Setup` | pass | owner created → `/Model`; afterwards `/Setup` → `/Login` |
| `/` | pass | → `/Chat` (model already accepted) |
| `/Agents` | pass | table seed (system, Ready, in Chat) + skanyxx-factory (system, not in Chat): only Details/Chat, no Edit/Suspend/Delete. Details of both show the "system agent" notice. New agent: browser blocks empty name and `Bad_Name`; `seed` and `skanyxx-crud-test` → "reserved" (400); created `crud-test-agent-skx` (A2A skill, stream) → listed, Ready; duplicate → 409 "already exists"; Edit (name read-only) → saved; fresh PUT 200, stale PUT 409 "changed since you opened it"; PUT/DELETE/suspend on seed and DELETE on factory → 409; Suspend → "Suspended", gone from Chat's agent list; Resume; Delete (confirm) → gone, GET 404; Chat button → `/Chat?agent=kagent%2Fseed` |
| `/Chat` | pass* | picker `kagent/seed`, analytics panel, New Chat. Message to `seed` → **502** with the fixed text "The agents cannot answer right now" — environment: the seed's memory MCP points at another worktree's Host (`:5287`) which answers 500 on `initialize` (not touched, rule). A real turn with a throwaway `crud-test-chat` agent made on the Agents page: **200, reply "PONG" in 6 s**, no console/network errors, no secrets; session and agent deleted |
| `/Library` | pass | personal card via API, search, detail, rename, lift to team (`history` API 404: not on this branch, memory-impl's D304) |
| `/ToolServers` | pass (after fix) | add dialog; `Bad_Name` blocked by the browser; `crud-test-ts` created ("Tool server created!"), duplicate → 409 message; deleted in cleanup |
| `/Studio` | pass | git not configured → explains what Studio needs |
| `/Plugins` | pass | Git / ZIP / catalog tabs, refresh |
| `/Sandboxes` | pass | experimental AX page (module off) |
| `/Debug` | pass | version + commit, health checks (3× Postgres, kagent, self) Healthy, modules |
| `/Model` | pass | Ollama / qwen3-coder:30b, key field empty; **not saved** (it edits `default-model-config`, the seed's model) |
| `/People` | pass | 4 invites (link shown once, gone after reload), revoke; later: save roles, reset link, disable ×3, enable |
| `/Org` | pass (after fix) | create/rename department and team, add member, invalid slug blocked by the browser, duplicate → 409 message |
| `/Entra` | pass | disabled, not saved (no tenant) |
| `/Audit` | pass | filter works; rows `agent.*_requested` + outcome for every agent write, `agent.write_refused`, `route.owner_refused` |
| `/Account`, `/Privacy` | pass | |
| Sign out | pass | → `/Login` |

### Employee / builder / supervisor (sidebar + direct URLs + APIs)

| Page | Employee | Builder | Supervisor |
|---|---|---|---|
| `/`, `/Chat`, `/Library`, `/ToolServers` (no add/delete), `/Account`, `/Privacy` | 200 | 200 | 200 |
| `/Agents` | 200, only `seed`, no New agent | 200, seed + factory, no New agent | 200, New agent |
| `/Studio`, `/Plugins` | 403 `/AccessDenied` | 200 | 200 |
| `/Sandboxes` | 403 | 403 | 200 |
| `/Debug`, `/Model`, `/People`, `/Org`, `/Entra`, `/Audit` | 403 | 403 | 403 |
| `GET /api/agents/kagent/skanyxx-factory` | 404 | 200 (prompt shown) | 200 |
| `GET /api/agents/kagent/seed` | 200, prompt hidden | 200, prompt shown | 200 |
| `GET /api/agents/options`, `POST /api/agents` | 403 | 403 | 200 / 201 (`crud-test-role`, deleted) |
| `DELETE seed`, `POST seed/suspend` | 403 | 403 | 409 |
| identity people/invites/audit/entra, `/api/model` | 403 | 403 | 403 |
| `POST`/`DELETE /api/toolservers` | 403 | 403 | 403 |

All pass; no secret in any page. Nav items match the role (no admin panels for employee/builder; supervisor gets
Studio, Plugins, Sandboxes).

### Anonymous

Every protected page → `/Login?ReturnUrl=…`; APIs 401; `/Privacy`, `/Offline`, `/Login` 200; `/Setup` → `/Login`
(done); `/ForgotPassword` 404 (no SMTP configured, by design); `/Invite`, `/ResetPassword` without a token 404. Pass.

## Bugs found and fixed

| # | Bug (seen in Chrome) | Root cause | Fix | Regression test |
|---|---|---|---|---|
| 1 | Invite accept (and password reset) answered **403** in Chrome; employees could not join | `Referrer-Policy: no-referrer` makes Chrome post the page's own form with `Origin: null`, which the origin guard refuses | `strict-origin` (D312) | test `Browser` sends the Origin a browser would; `AccountRecoveryPageTests`, `PeoplePageTests` |
| 2 | Signed-in person without the role bounced to `/Login` and back (redirect loop) | cookie access-denied path was `/Login` | `/AccessDenied` page, 403 (D314) | `FirstHourTests`, `SandboxesPageTests`, `StudioTests` |
| 3 | Tool Servers: every create failed ("empty reference string" from kagent) | page/API sent `{remoteMCPServer: {name, endpoint}}`, not kagent's `metadata` + `spec.url` | kagent's shape, validation, 409/400/502 mapping, owner-only add/delete (D313) | `ToolServersAndAgentsApiTests` |
| 4 | Org slug / Tool Server name / Studio name checks skipped by the browser, console error | `pattern` with an unescaped `-` is invalid under the `v` flag | escape `-` | `PagePatternTests` |
| 5 | Agents table: suspended agent shown "In Chat: yes" (Details said no, Chat skipped it) | column used only `inChat` (= labelled merged) | column shows `no (suspended)` | `AgentsApiTests.TheInChatColumn_SaysNoForASuspendedAgent` |

1–4 were already fixed on `memory-impl` (f8b4a6b); ported here without its Agents part (this branch replaces that
page) — D258. All five re-checked in Chrome after the rebuild (`recheck-*.png`, walk run 2).

Not fixed (noted): the open Details panel is not refreshed after Suspend/Resume (shows the old status until
reopened); the audit API answered at most 50 rows even with `take=500`.

## kagent CRUD comparison (kagent UI :8080 + controller :8083 vs Skanyxx)

Throwaway objects only (`crud-test-*`), all deleted; `seed`/`skanyxx-factory` resourceVersions 706/795 before and
after; final `kubectl get agents,modelconfigs,remotemcpservers,secrets | grep crud-test` → none.

| Surface | Operation | kagent | Skanyxx | Same/different | Note |
|---|---|---|---|---|---|
| Agents | create — fields | name, namespace, type (Declarative/BYO), run in sandbox, ADK runtime, description, instructions, model, stream, service account, share-link tools, embedding model / memory TTL, event compaction, skills from OCI/git/S3 | namespace (fixed), name, description, prompt, model, runtime, stream, allow-listed MCP tools, agents as tools, digest-pinned skills, A2A skills | different | **by design** (D251: no BYO, sandbox, SA, memory, git/S3 skills, Secrets) |
| Agents | create — defaults | prompt prefilled ("You're a helpful agent, made by the kagent team…"), stream off, sandbox off | prompt empty (required), stream off, runtime "kagent's default" | different | Skanyxx asks for a prompt instead of a vendor default |
| Agents | create — validation | empty: "Agent name is required / Description is required / Please select a model"; `Bad_Name`: RFC 1123 text | empty/`Bad_Name`: browser pattern messages; API 400 lists every problem; reserved names 400 | different | kagent requires a description, Skanyxx does not; kagent allows `.` in names, Skanyxx DNS-1035 (by design, D251) |
| Agents | create — duplicate | generic "Failed to create Agent in Kubernetes", stays on form | 409 "An agent named '…' already exists in 'kagent'." | different | Skanyxx clearer |
| Agents | create — stored spec | `{type, description, declarative: {runtime: go, systemMessage, modelConfig}}`, no labels | identical spec keys and values; labels `skanyxx.dev/managed-by: skanyxx-agents`, `skanyxx.dev/merged: "true"` | same spec | labels by design (D252/D253: in Chat, never adopted by the reconciler) |
| Agents | read / list | both list both agents | kagent-made shows Source `external`, not in Chat; Skanyxx-made `direct`, in Chat; Details panel with conditions as type/status/reason only | same | condition messages hidden by design (C-4) |
| Agents | update | kagent's edit of the Skanyxx-made agent changes description, keeps labels | Skanyxx's edit of the kagent-made agent changes description only, rest of spec byte-identical | same | |
| Agents | update — concurrency | last writer wins | stale form after the kagent edit → 409 "The agent changed since you opened it…", kagent's value kept | different | by design (C-2) |
| Agents | suspend / resume | none; suspended agent shows "Agent not Ready" | Suspend sets `deployment.replicas: 0`, Resume removes it | missing in kagent | Skanyxx feature (D257) |
| Agents | delete | menu → confirm "Are you sure…" → gone | Delete → confirm → gone; again → 404 | same | |
| Agents | seed / factory | editable/deletable in kagent | 409 for everyone, no buttons | different | **by design** (D252) |
| Agents | roles | none (unsecure) | owner/supervisor write, builder read-only, employee Chat agents only | different | **by design** (D254) |
| Model configs | create | 10 providers, model list, generated name, API key → Secret | no create: one model (`default-model-config`) on `/Model`, 3 providers (OpenAI, Anthropic, Ollama) | missing in Skanyxx | by design for the first hour (D099); more providers listed as "not yet" in first-hour.md |
| Model configs | update | provider/model locked on edit; key rotation **failed** ("Failed to update ModelConfig") | `/Model` validation: empty model id → 400 with reason, kagent untouched (resourceVersion same) | different | kagent UI bug, not Skanyxx |
| Model configs | delete | row menu → deleted, its Secret removed | none | missing in Skanyxx | by design |
| Tool servers | create | defaults name `tool-server`, timeout 5s, SSE read 300s, protocol **SSE**; bad name/url: two messages | name/namespace/URL, protocol **STREAMABLE_HTTP**, timeout 30s; bad URL blocked by browser, `ftp://` → 400 with reason | different | different protocol default; Skanyxx offers remote only (by design, D313) |
| Tool servers | read / list | both list both servers (0 tools, unreachable host) | same list | same | |
| Tool servers | update | no edit, only "Remove server" | no edit | same | |
| Tool servers | delete | menu → removed | Delete → confirm → removed; again → 404 "No such tool server." | same | owner only (by design) |
| Secrets / keys | all | no secrets surface (Secrets only as a side effect of model keys) | agent memory secrets: issue (shown once), read (metadata only), revoke | Skanyxx only | secrets never shown again (by design) |
| Sessions | list / delete | API `/api/sessions?user_id=` | Chat history per person; the test session deleted via kagent API | same data | |
| Memory | list | `/api/memories` → 500 (`relation "memory" does not exist`) | Library cards (Postgres) | different | kagent memory unused by design (cards only) |

## Tests

`dotnet build Skanyxx.sln -p:UseSharedCompilation=false` 0 warnings, 0 errors; `dotnet test --solution Skanyxx.sln`
(after all fixes, one run): **2277 passed, 0 failed, 0 skipped** — Host 6m45s, Identity, Memory, Tickets, Sandboxes
(Testcontainers on OrbStack).
