# Chrome test — branch `memory-impl` (2026-10-28)

End-to-end walkthrough of the whole Skanyxx UI in real Google Chrome (`/Applications/Google Chrome.app`, Playwright
`channel: "chrome"`, scripts in `/tmp/skx-e2e`, outside the repo), then a CRUD comparison against kagent's own UI and
controller API. Screenshots (140): `~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/evidence/chrome/memory-impl/`
(`<role>-<page>.png`, `crud-*.png`).

**Result: pass** after the fixes below. Full suite: see "Tests". The Host and the throwaway Postgres were stopped at the end; kagent and its port-forwards were left running.

## What was started (and stopped at the end)

| Piece | How |
|---|---|
| Postgres | throwaway container `crud-test-skanyxx-pg` (`postgres:16-alpine`, `127.0.0.1:55499`), recreated empty before the final run |
| Host | this worktree, `dotnet build -p:UseSharedCompilation=false`, `dotnet run --no-launch-profile` with the env of `scripts/dev/first-hour.sh host` (Development, `127.0.0.1:5391`, `KAgent` at `localhost:8083`, tickets from the sample file) |
| kagent | already running (controller + UI 0.10.2 via the existing port-forwards on 8083 / 8080) — not started or stopped |
| Not started | Gitea / Studio git (the Studio reconciler would re-apply or remove Studio-managed agents such as `skanyxx-factory`), `seed-secret` (it rewrites the seed's Secret and labels `seed`), AX (sandboxes stay off) |

Environment limits, not branch bugs: Ollama is not running on this machine and the seed's memory MCP points at another
worktree's Host (`:5287`, whose Postgres is down, so its `/mcp/memory` answers 500). A chat turn therefore cannot
complete: Skanyxx creates the kagent session, kagent fails the task, Skanyxx answers **502 "The agents cannot answer right
now"**, deletes the session it created and shows the error in the chat (checked: no orphan sessions in kagent). Fixing that
would mean changing `seed`, which is off limits.

## Bugs found and fixed

| # | Bug (seen in Chrome) | Root cause | Fix | Regression test |
|---|---|---|---|---|
| 1 | **Accepting an invite and finishing a password reset were 403** in a real browser. | `/Invite` and `/ResetPassword` answered `Referrer-Policy: no-referrer`; under it Chrome posts the form with `Origin: null`, which `OriginGuardMiddleware` refuses. Tests passed because the test `Browser` sent no `Origin`. | Both pages answer `strict-origin` (Referer carries only the origin, never the token) — D312. | `Browser.SubmitAsync` now sends the `Origin` a browser would (`null` under `no-referrer`), so every Razor form test goes through the origin guard; Invite/Reset tests assert `strict-origin` and failed with 403 before the fix. |
| 2 | **Every owner-only page looped** (`ERR_TOO_MANY_REDIRECTS`) for employee, builder and supervisor. | Cookie `AccessDeniedPath` was `/Login`, which sends a signed-in person straight back to `ReturnUrl`. | New `/AccessDenied` page, always **403** ("Your account does not have access…") — D314. | People/Studio/Model/Debug/Plugins/Sandboxes tests follow the refusal to `/AccessDenied`; the People test follows it and asserts 403. |
| 3 | **Creating a tool server from the Tool Servers page always failed** (kagent: "empty reference string"); the namespace field was ignored; delete errors were silent. | Skanyxx posted `{name, endpoint}`; kagent wants the RemoteMCPServer object (`metadata` + `spec.url`). | `POST /api/toolservers {name, namespace, url}` builds kagent's shape (STREAMABLE_HTTP), validates (400 with reason), 409 on duplicates, 502 fixed text on kagent errors; `DELETE` 204/404. Page: only the remote type, namespace sent, errors shown, add/delete only for the owner, kagent/MCP-provided names and descriptions escaped (they went into `innerHTML`) — D313. | `ToolServersAndAgentsApiTests` (shape stored in the fake kagent, 409, 400 ×5, 502, delete 204/404). |
| 4 | **Agents → Details went nowhere** (link to the owner-only `/Debug`, which ignores it; 403 loop for everyone else); `GET /api/agents/{id}` was always 404. Agent names/descriptions went into `innerHTML` unescaped. | Route used an id kagent does not have. | `GET /api/agents/{namespace}/{name}` + a Details dialog (type, status, description, MCP tools, labels); text escaped — D313. | `AgentDetails_ByNamespaceAndName_ForAnyoneSignedIn_UnknownIs404`; `TheAgentsApi_IsReadOnly_EvenForTheOwner` kept (405 for POST/PUT/DELETE). |
| 5 | Chrome console: "Pattern attribute value … is not a valid regular expression" on Org, Studio, Tool Servers — client-side slug/name checks silently off. | Browsers compile `pattern` with the `v` flag, where an unescaped `-` in a class is a syntax error. | `\-` in those classes — D314. | `PagePatternTests` (every literal `pattern` in `Pages/` + `CardFormat.KeyPattern`). |
| 6 | (found by the full suite, not Chrome) `PluginStudioTests.ValuesStoredWhileACloseLands_AreNotPurgedByIt` failed on this branch **and on clean HEAD**, deterministically for a while, then not. | The test stamped `updated_at` with Postgres's `now()` while the close compares it with the Host's `DateTime.UtcNow` a few ms earlier; a container clock a few ms behind made the renewed row look older than the close. Production uses one clock (the store handler writes `DateTime.UtcNow`). | The test stamps the row with the Host clock, as the store handler does. | the test itself (3/3 green after). |
| 7 | Library card links rendered in the browser's default dark blue on black (unreadable). | No link colour in the dark theme. | `td a, p a, dd a` use a readable accent. | visual (screenshots). |

## QA

One independent review round (code-reviewer + security brief, diff only): **PASS**, no blockers or majors; full text in
`~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/review/code-reviewer-chrome-memory-impl-1.md`. All eight
minor/nit findings were applied: kagent 4xx on a tool-server create → 400 (not "try again"); a URL carrying user:password is
refused; a non-string `ref` from kagent cannot 500 the duplicate check; the Details dialog handles a network failure, moves
focus in and restores it; `GET /api/agents/{ns}/{name}` maps kagent failures to 502; `PagePatternTests` also catches a `-`
right after a range (`[a-z-_]`); a test that only the owner gets Add/Delete on Tool Servers. Re-checked in Chrome
(`recheck-*.png`): focus `agentModalClose` → restored to Details; user-info URL → 400 with the reason; create + delete
`crud-test-recheck` from the page.

## Pages visited (final run)

115 checks, all pass. "Expected 4xx" are deliberate: 403 for role refusals, 409 duplicate tests, 400 invalid input tests,
404 `/api/sandboxes/*` (module off), 401 anonymous APIs, 502 chat (environment, above).

### Owner (`e2e-test-owner@example.com`, created on `/Setup`)

| Page | Result | What it showed / what was done |
|---|---|---|
| `/` (before setup) | pass | redirects to `/Setup` |
| `/Setup` | pass | owner created, signed in, → `/Model` (a password containing the email's local part is refused, as designed) |
| `/` | pass | → `/Chat` (kagent already has an accepted model) |
| `/Agents` | pass | `seed`, `skanyxx-factory` cards; Refresh; **Details** dialog for both; **Chat** → `/Chat?agent=kagent/seed` with seed preselected |
| `/Chat` | pass (env) | picker: `kagent/seed` only (merged agents); analytics panel toggled; message to seed sent → 502 shown in chat (environment); New Chat |
| `/Org` | pass | create department + team (`e2e-test-*`), rename both, add the owner to the team, duplicate slug → "already exists" (409), invalid slug blocked by the browser |
| `/Library` | pass | `e2e-test-card` (personal) created via API, searched, detail opened, **renamed**, history 200, **lifted** to `team:e2e-test-team` (targets: team, department, company) |
| `/ToolServers` | pass | list; Add dialog open/cancel; invalid name blocked by the browser; **create `crud-test-ts`** ("Tool server created!"); duplicate → "already exists"; **delete** via × (confirm) → gone in kagent |
| `/Studio` | pass | page explains the studio needs the agent repo (git not configured in this run) |
| `/Plugins` | pass | Catalog / Git URL + commit / Upload zip tabs, Refresh |
| `/Sandboxes` | pass | Experimental banner, module off (no AX) |
| `/Debug` | pass | version `1.0.0+de89d7f…`, mode Server, all health checks Healthy incl. kagent, module table |
| `/Model` | pass | Ollama / `qwen3-coder:30b` / base URL shown, API key field empty; **not saved** (it edits `default-model-config`, which seed uses); an empty model id is refused with a validation message and kagent's object is unchanged (resourceVersion 596 → 596) |
| `/People` | pass | invites for employee, builder, supervisor (link shown once, gone after reload); a 4th invite revoked; later: save roles, password reset link (shown once), disable ×3, enable ×1 |
| `/Account` | pass | name, email, roles; Microsoft sign-in not enabled |
| `/Entra` | pass | form shown, not saved (no tenant) |
| `/Audit` | pass | filter form works (the audit table holds `invite.created`, `org.*`, `person.*`, `route.owner_refused`, …) |
| `/Privacy` | pass | template page |
| Sign out | pass | → `/Login` |

No page showed a token or secret (scan for `skx_mem_`/`skx_inv_`/`skx_rst_`/JWT/`sk-…`/`Bearer …`; invite and reset links
are shown once to the owner by design).

### Employee / builder / supervisor (accepted their invites in Chrome)

| Page | employee | builder | supervisor |
|---|---|---|---|
| `/` → `/Chat`, `/Agents`, `/Chat`, `/Library`, `/ToolServers`, `/Account`, `/Privacy` | 200 | 200 | 200 |
| `/Studio`, `/Plugins` | **403** `/AccessDenied` | 200 | 200 |
| `/Sandboxes` | **403** | **403** | 200 |
| `/Debug`, `/Model`, `/People`, `/Org`, `/Entra`, `/Audit` (direct URLs) | **403** | **403** | **403** |
| Nav | Agents, Chat, Library, Tool Servers | + Studio, Plugins | + Sandboxes |
| Owner APIs (`/api/identity/people`, `invites`, `audit`, `entra/settings`, `/api/model`, seed secret) | 403 | 403 | 403 |
| `POST`/`DELETE /api/toolservers` | 403 | 403 | 403 |
| `/api/studio`, `/api/plugins/catalog` | 403 | 200 | 200 |
| Tool Servers page | list only, no Add / × | same | same |

### Anonymous

Every app page → `/Login?ReturnUrl=…`; `/Privacy`, `/Offline`, `/Login` 200; `/Setup` after setup → `/Login`; `/Invite`
and `/ResetPassword` without a token 404; `/ForgotPassword` 404 with SMTP off (by design, D156; Login hides the link);
APIs 401.

## kagent CRUD comparison

kagent controller + UI 0.10.2. All objects named `crud-test-*`; all deleted; `seed`, `skanyxx-factory`,
`default-model-config` and `skanyxx-memory-seed` have the same resourceVersions before and after (706 / 795 / 596 / 449111).

| Surface | Operation | kagent | Skanyxx | Same/different | Note |
|---|---|---|---|---|---|
| Agents | create | UI form: name, namespace (default `default`), type, sandbox, runtime, description (required), instructions (prefilled kagent text), model (required), stream, service account, tools, memory, skills. Empty form → "Agent name is required / Description is required / Please select a model"; `Bad_Name` → RFC 1123 message | none: `POST /api/agents` 405; agents are proposed in the Studio and merged (D110) | different — **by design** | Studio was not exercised (needs git; its reconciler would touch Studio-managed agents) |
| Agents | read/list | list + chat links | list (all kagent agents) + **Details** dialog (type, status, description, MCP tools, labels); `GET /api/agents/{ns}/{name}` | same data, less of it | Skanyxx shows no model/instructions (read-only summary) |
| Agents | update | edit page (`/agents/new?edit=true…`): description changed → stored | none (PUT 405) | different — **by design** (Studio) | |
| Agents | delete | list menu → Delete → confirm; CR gone | none (DELETE 405); the card disappears after kagent's delete, Details → 404 | different — **by design** | |
| Agents | chat | every agent has a chat link | only agents labelled `skanyxx.dev/merged` (seed); `crud-test-agent` not offered | different — **by design** (D100/D4) | |
| Model configs | create | UI: provider (10), model list (28 for OpenAI), generated name (editable via pencil), namespace, API key (→ Secret `crud-test-model`, key `OPENAI_API_KEY`). Empty → "Please select a Provider and Model" | none: Skanyxx edits only `default-model-config` (`/Model`, providers OpenAI / Anthropic / Ollama) | different — **by design** (D099, one owner model) | Skanyxx lacks Azure OpenAI, Bedrock, Gemini, … (first-hour.md "Not yet") — **missing feature** |
| Model configs | read | list with provider/model, details | `/Model` shows provider, model, base URL; key never shown | same for the default model | |
| Model configs | update | edit: provider/model locked, key rotation stored (Secret updated); one run answered "Failed to update ModelConfig" although the Secret changed | `/Model` save (not exercised live: would change seed's model); invalid input refused before kagent | different | kagent's flaky update message is kagent-side |
| Model configs | delete | row → Delete → confirm; Secret removed with it | none | different — **by design** | |
| MCP / tool servers | create | UI: name (default `tool-server`), namespace, URL or command, **SSE by default** (Streamable HTTP off), headers JSON, timeout 5s, SSE read 300s, terminate on close. Bad name/URL → "Please enter a valid URL…", "Name must conform to RFC 1123" | **after fix 3**: name, namespace, URL; always STREAMABLE_HTTP; browser + server validation ("must be lowercase…", "absolute http or https"); duplicate → 409 message | **different** | stored spec — kagent: `protocol: SSE, timeout: 5s, sseReadTimeout: 5m0s`; Skanyxx: `protocol: STREAMABLE_HTTP, timeout: 30s` (kagent API default). Skanyxx has no headers / timeouts / stdio servers — **missing feature** |
| MCP / tool servers | read/list | list with tool counts, expand for tools, search | cards with tool names (escaped), status badge | same objects listed | both list servers created by the other |
| MCP / tool servers | update | none in 0.10.2 (only "Remove server") | none | same | |
| MCP / tool servers | delete | "…" → Remove server → Confirm | × → confirm (owner only); 404 "No such tool server." on a second delete | same result | Skanyxx lets the owner delete `skanyxx-memory-seed` too (behind a confirm) — a **difference worth an owner decision**: kagent allows it as well, but in Skanyxx it silently breaks the seed's memory |
| Secrets / keys | all | no secrets UI; Secrets are side effects (model API key, MCP headers) | agent memory secrets: `POST /api/memory/agents/{id}/secret` (owner) returns `skx_mem_…` **once**, `GET` shows only `hasSecret`/dates, `DELETE` 204 | different — **by design** (secrets never shown again) | no Skanyxx UI page for agent secrets (API + `first-hour.sh seed-secret` only) — **missing feature** |
| Sessions | create/read/delete | chat sidebar sessions per agent, delete in UI | created per person in kagent on the first turn; a failed first turn deletes it; no session list/delete in Skanyxx Chat (New Chat starts over) | **different** | Skanyxx has no "previous chats" list — **missing feature** (sessions are summarised into personal cards instead, D301) |
| Memory | all | agent long-term memory (`/api/memories`; here 500 "relation memory does not exist" — kagent's DB lacks the table) | company knowledge as cards (`/Library`: search, detail, rename, lift, history) | different — **by design** (AGENTS.md: not kagent TTL memory) | kagent's memory API is broken on this cluster (kagent-side) |
| Prompt libraries | all | `/prompts` (kagent-builtin-prompts), create | none | different | **missing feature** in Skanyxx (not on the roadmap) |
| Agent harness | create | `/agents/new-harness` (OpenClaw / Hermes) | none | different | out of scope (AX is the sandbox runtime, D063) |

## Tests

`dotnet build Skanyxx.sln -p:UseSharedCompilation=false` (0 warnings, 0 errors), then `dotnet test Skanyxx.sln` once at
the end: **2308 / 2308 passed**, 0 failed, 0 skipped (Host 6m26s, Identity, Memory, Sandboxes, Tickets; Testcontainers on
OrbStack). New: `ToolServersAndAgentsApiTests` (10), `PagePatternTests` (one per page + 10 checker cases + card key).

## Not covered

- Studio proposals / merge, plugin install (need the bundled git; Studio reconciling would touch `skanyxx-factory`).
- A completed chat answer (no model server running; seed's memory MCP points at another Host).
- Sandboxes runs (AX off), Entra save (no tenant), SMTP flows (forgot password by email).
