# Chrome test: `chat-context` (2026-10-28, full walkthrough)

## What was started (all stopped afterwards)

- **Postgres:** throwaway container `crud-test-pg-chatctx` (`postgres:16-alpine`) on `127.0.0.1:55497`, recreated before
  each round and removed at the end.
- **Host:** `dotnet build Skanyxx.sln -p:UseSharedCompilation=false`, then `dotnet run --no-build` on
  `http://127.0.0.1:5397` with the env that `scripts/dev/first-hour.sh host` sets: Development, `KAgent` at
  `localhost:8083` (300 s turns), the sample tickets file, all three connection strings on the throwaway Postgres.
  `Identity:PublicBaseUrl` = `http://localhost:5397`. No Gitea, so the Studio is off (the page explains this).
- **kagent:** the existing controller (`:8083`) and UI (`:8080`). It, its port-forwards and the cluster were not stopped.
  `seed` and `skanyxx-factory` were never written (resourceVersions 706/795 before and after).
- **Ollama:** not running at the start, so it was started for the chat turns and stopped at the end.
- **Throwaway kagent objects:** `crud-test-chat` (a tool-less Agent labelled `skanyxx.dev/merged`, for a chat turn
  that works, see "Seed" below), `crud-test-xss` (the XSS probe), `crud-test-agent-k/-s`, `crud-test-model`,
  `crud-test-ts-kagent/-skx`, and their sessions. All deleted; `kubectl get agents,modelconfigs,remotemcpservers,secrets`
  shows no `crud-test-*`, and no session of the run is left in kagent.
- **Browser:** Google Chrome 156 through Playwright 1.64 (`channel: "chrome"`, headless, 1400×900), scripts in a temp
  directory outside the repo (copied to the evidence folder's `_scripts/`).

Screenshots (184), `walk-results.json`, `kcrud-results.json`, the scripts and logs:
`~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/evidence/chrome/chat-context/full/`.

## Seed

The chat message to `seed` was sent from the Chat page and answered **502** ("The agents cannot answer right now"). kagent
reports `failed to init MCP session: calling "initialize": … Internal Server Error`: the seed's memory MCP
(`skanyxx-memory-seed`) points at the Host of another checkout on `host.docker.internal:5287`, whose secret is not this
run's. Fixing it means rewiring the seed, which this run must not touch. The Chat page showed the error message and
stayed usable. The same turn with `crud-test-chat` worked (below).

## Pages (final round: 144 steps)

Every step records the HTTP status, console errors, 4xx/5xx responses from the Host and kagent UI, secrets in the DOM
(`skx_mem_/inv_/rst_/sbx_`, JWTs, `Bearer …`, `sk-…`, `ghp_…`), and a screenshot. **No secret was rendered**. The
invite and reset links appear only on the owner's own People page, once, as designed. **No admin nav item reached a
non-owner, and every admin page was refused.**

### Owner

| Page | Clicked / did | Result |
|---|---|---|
| `/Setup` (fresh DB) | form, created the owner | pass → `/Model`; a second `/Setup` goes to `/Chat` |
| `/` | | pass → `/Chat`; the nav has all 20 items |
| Dashboard | Refresh | pass (3 agents, kagent online) |
| Investigate | Create Custom (open/close); 9 templates listed | pass (templates not started: each starts an agent run) |
| Agents | cards, Refresh, Details (seed) → `/Debug?agent=seed`, Chat (seed) → picker `kagent/seed` | pass; no create/edit/delete here (D109) |
| Chat | picker, History, Analytics toggle, mask toggle, **message to `seed`**, reload, New Chat | **seed 502** (environment, above); page pass |
| Chat with `crud-test-chat` + `?ticket=SDB-58` | send, reload, History, ticket view | pass: 200 in 3 s; after a reload the same session, 2 bubbles; History lists it; `/api/chat/tickets/SDB-58` holds it |
| Chat `?ticket=SDB-58` | `/api/chat/sessions?mine=true`, `/api/chat/cache` | pass |
| Library | `e2e-test-card` (API PUT), search, 3 scope filters, detail, invalid key (blocked), rename | pass (lift not done: no card delete to clean up a team or company card) |
| Tool Servers | Refresh, Add dialog open/close, invalid name + URL (blocked), **create `crud-test-ts-skx`**, delete | pass after fix 1 (201, in the controller; deleted) |
| Cloud Tools | Search/REPL tabs, providers, Refresh, Refresh auth, Test Azure CLI | pass (no az CLI here: "Not Found") |
| Analytics | Refresh | pass |
| Alerts | 4 filters, Refresh, New Alert open/cancel, create `e2e-test-alert` | page pass; **create 501** (leftover, below) |
| Hooks | New Hook open/cancel, create `e2e-test-hook` | page pass; **save 500** (leftover, below) |
| Studio | | pass ("needs the agent repository": no Gitea) |
| Plugins | Catalog / Git / Zip tabs, Refresh | pass |
| Sandboxes | | pass (off, says so) |
| Debug | log filter, Refresh system info, Run `get agents -n kagent`, Clear | page pass; **Run 404** (leftover, below) |
| Settings | 5 sections, Add Connection open/cancel, create `e2e-test-conn`, delete it | pass |
| Model | read only | pass (not saved: `default-model-config` is the model `seed` runs on) |
| People | 3 invites (shown once, gone after reload), revoke one; later: save roles, reset link (once), disable ×3, enable | pass |
| Org | invalid slug (blocked in the browser after fix 1), dept + team create/rename, member add/remove, duplicate dept | pass (duplicate → "already exists", 409) |
| Microsoft sign-in | read (secret field empty) | pass (not saved: no tenant) |
| Audit | list, filter | pass |
| Account, Privacy, Offline, AccessDenied (403 page) | | pass |
| Sign out | | pass → `/Login` |

### Employee, builder, supervisor (each accepted an invite in Chrome, then opened every URL)

| Role | Nav shows | Reached (200) | Refused → `/AccessDenied` (403) |
|---|---|---|---|
| employee | Dashboard, Investigate, Agents, Chat, Library, Tool Servers, Analytics, Alerts, Hooks | those + Account, Privacy | Studio, Plugins, Sandboxes, Cloud Tools, Debug, Settings, Model, People, Org, Entra, Audit |
| builder | + Studio, Plugins | + Studio, Plugins | Sandboxes, Cloud Tools, Debug, Settings, Model, People, Org, Entra, Audit |
| supervisor | + Studio, Plugins, Sandboxes | + Sandboxes | Cloud Tools, Debug, Settings, Model, People, Org, Entra, Audit |

All three: pass. Admin APIs answer 403 (people, invites, audit, Entra, settings, system info, connections, Cloud Tools,
`debug/logs`, agent/tool-server/hook/alert writes, REPL, scale). By design: builders and supervisors read `api/studio`
and `api/plugins/catalog`; a supervisor reads an agent secret's **status** (`hasSecret`, never the value); `api/sandboxes/runs`
is 404 while sandboxes are off. The employee sees the agent picker, and the owner's chat answers 404 to them.
(`DELETE /api/chat/cache` answered 400 to my probe because it sent an empty JSON body.)

### Anonymous

Every app page → `/Login?ReturnUrl=…`; `/Setup` after setup → `/Login`; Login, Privacy, Offline load; AccessDenied 403;
`/Invite` and `/ResetPassword` without a token 404 and `/ForgotPassword` 404 with SMTP off (D156), all by design; every
API → 401.

## Bugs found and fixed (root cause + regression test, re-checked in Chrome)

1. **Invite accept and password reset were 403 in Chrome; Tool Servers create always failed; three `pattern`s were
   ignored (D245).** `Referrer-Policy: no-referrer` makes a browser post the form with `Origin: null`, which the origin
   guard refuses: no invite could be accepted in a browser, so no employee/builder/supervisor could join. The Tool
   Servers form's create sent `{name, endpoint}` and kagent 0.10 answered "empty reference string". `[a-z0-9-]` is a
   syntax error under the `v` flag Chrome uses for `pattern`, so Chrome skipped the check (Org slug, Studio name, Tool
   Servers name). memory-impl and agents-crud had already fixed exactly these (D312–D313, D258), so this is a port
   of agents-crud `e07d325` (its access-denied part is D243 here): `strict-origin`, kagent's RemoteMCPServer shape with
   validation and 409/400/502 mapping, escaped patterns, a test `Browser` that posts the `Origin` Chrome would send.
   Tests: `PagePatternTests`, `ToolServersAndAgentsApiTests`, `PeoplePageTests`, `AccountRecoveryPageTests`.
2. **Stored XSS on the Agents page (D246).** Cards were built with `innerHTML` from the Agent's name and description.
   A throwaway Agent whose description was `<img src=x onerror="window.__xss=1">` ran script in the owner's session
   (`bug-agents-xss-before.png`); anyone who can edit an Agent in kagent (its UI has no sign-in here) or get a studio
   proposal merged could do it. Now escaped, with `data-*` buttons instead of inline `onclick` strings (`…-after.png`:
   shown as text, nothing ran). Test: `KAgentDataEscapingTests` (Agents and Tool Servers).
3. **Debug was open to every role (D247).** Only its nav link was owner-only: an employee opened `/Debug` by URL and
   read `GET api/debug/logs`, and anyone signed in could write `POST api/debug/log`. The page and the API are now the
   owner's, and Agents shows Details (a `/Debug` link) only to the owner. Tests:
   `FirstHourTests.Employee_OnAPageTheirRoleLacks_GetsA403Page_NotALoginLoop("/Debug")`,
   `Employee_CannotReadOrWriteTheDebugLog_AndTheAgentsPageOffersNoDetailsLink`.
4. **Cloud Tools was in every role's nav, but its API is owner-only (D116), and public pages logged a 401 (D248).** Non-owners
   got a page of 403s; the page and its nav link are now the owner's. `site.js` read the signed-in-only theme on
   Privacy/Offline/AccessDenied; it now skips it without a signed-in header. Tests: the theory above with
   `"/CloudTools"`, `SiteScriptTests`.

Earlier on this branch (first Chrome run, already committed): D243 (role-gated pages looped through `/Login`) and
D244 (Chat reopened a conversation as the wrong kagent user and could not read Go-runtime events).

## Known, not fixed here

- **Leftover SRE pages (D001; reshaped or removed on `phase1-cleanup-rbac`):** Alerts create → 501 and Hooks save → 500
  (they call `/api/alerts` and `/api/hooks` on kagent, which 0.10 does not have), Debug "Run" → 404 (calls
  `/api/kubernetes/execute`, which no module serves), and Analytics logs "unexpected shape" for `/api/sessions`. Per
  AGENTS.md these pages are not grown here.
- **The legacy agents API** (owner break-glass, D109) is broken against kagent 0.10 (CRUD table). agents-crud replaces
  it with a real Agents CRUD (D250–D257).
- **Seed chat** fails in this environment (above).
- From the QA review (`review/qa-chat-context-chrome-1.md`, verdict PASS, low findings only): the owner can delete or
  shadow a Studio-managed tool server by name; the service worker pre-caches owner-only routes (`/Debug`,
  `/CloudTools`, and already `/Settings`), so a non-owner's pre-cache fails; a kagent 409 after the duplicate check maps
  to 400. All of these predate this run or are in ported code shared with memory-impl and agents-crud.

## kagent CRUD comparison

kagent UI = `localhost:8080` in Chrome; Skanyxx = the UI where it has one, otherwise the owner API; "controller" = what
`GET localhost:8083/api/...` (or `kubectl`) shows afterwards. Only `crud-test-*` objects, all removed.

| Surface | Operation | kagent | Skanyxx | Same/different | Note |
|---|---|---|---|---|---|
| Agents | create (fields) | form: name, namespace, type, sandbox, runtime, description, instructions, model, stream, service account, memory, skills (OCI/git/S3)… | no create on `/Agents` (Chat, Details only) | different, **by design** | D109: the Studio (PR gate) creates agents; it needs Gitea, off here. agents-crud adds direct CRUD (D250) |
| Agents | create (defaults) | system message "You're a helpful agent, made by the kagent team…", stream off | owner API: `modelConfig: default-anthropic` | different | |
| Agents | create (validation) | empty → "Agent name is required / Description is required / Please select a model"; `Bad_Name` → RFC 1123 message | owner API `Bad_Name` → **500** with kubectl's stderr | different, **broken** | legacy API; agents-crud returns 400 |
| Agents | create (stored) | v1alpha2 `{type, description, declarative: {runtime: go, systemMessage, modelConfig}}` | owner API → 201, but the stored spec is only `{type, description}`: **prompt and model dropped** | different, **broken** | it applies v1alpha1 YAML through kubectl; the conversion drops the fields. agents-crud replaces it |
| Agents | list/read | listed | kagent-made agent listed with its description, and after a kagent edit with the new one | same | `GET /api/agents/{name}` answers 404 (legacy route); Details → Debug (owner) |
| Agents | update | edit form changes only the description (rest of spec equal) | no UI; owner API status "paused" → 404 (scales `<name>-agent`, which 0.10 does not create) | different, **broken** (legacy) / by design (no UI) | |
| Agents | delete | menu → "Are you sure… cannot be undone" → gone | owner API: `DELETE /api/agents/crud-test-agent-k` → 204, gone; `kagent%2F…` form → 404 | same result | |
| Agents | protected | — | `seed`/`skanyxx-factory` never written by either | by design | Skanyxx never offers writes on them |
| Model configs | create | providers Anthropic…SAPAICore; OpenAI shows 28 models; name generated (`openai-gpt-3-5-turbo`); API key → kagent creates Secret `crud-test-model` | no list or create: `/Model` edits only `default-model-config` (providers OpenAI, Anthropic, Ollama) | different, **by design** | one install-wide model in Skanyxx |
| Model configs | validation | empty → "Provider and Model selection is required" | empty model id → "'model' must not be empty…" (400); `default-model-config` unchanged (rv 596 → 596) | same idea | |
| Model configs | read | spec `{model, apiKeySecret, apiKeySecretKey, provider, openAI}` | `/Model` shows provider + model; **key field always empty** | by design | secrets never shown |
| Model configs | update | edit: provider/model locked; save → "Failed to update ModelConfig" (key Secret rotated anyway) | not done (would change the seed's model) | — | kagent UI bug, not ours |
| Model configs | delete | delete → controller 404; its Secret gone | — | different, by design | |
| Tool servers (MCP) | create (fields/defaults) | name `tool-server`, timeout 5 s, SSE read 300 s, streamable off (**SSE**), terminate on close | name, namespace `kagent`, URL; **streamable HTTP**, timeout 30 s (kagent default) | different | protocol default differs |
| Tool servers | create (validation) | bad name + URL → "Couldn't create server / valid URL with protocol / RFC 1123" | browser: "Please match the requested format", "Please enter a URL"; API `ftp://` → 400 "absolute http or https" | same idea | after fix 1 |
| Tool servers | create | → `/mcp` "MCP server created" | → 201 `kagent/crud-test-ts-skx`, in the controller | same | **was broken before fix 1** |
| Tool servers | list | both servers, 0 tools | both servers | same | |
| Tool servers | update | no edit (menu has only "Remove server") | no edit | same | |
| Tool servers | delete | menu → Remove → gone | card × → gone; again → 404 "No such tool server." | same | |
| Secrets | all | no surface (Secrets only as a side effect of a model key) | agent memory secrets: issue (value shown once), status (no value), revoke → `hasSecret: false` | different, **by design** | secrets never shown again |
| Sessions | create | kagent UI chat with `crud-test-chat`: no session found afterwards for `admin@kagent.dev` | Chat turn → 200, session in kagent and in Skanyxx's index | not verified on kagent's side | |
| Sessions | list | per user in kagent | History lists only the person's own chats; kagent-UI sessions not shown | different, **by design** | personal chats (D237) |
| Sessions | delete | controller DELETE → 200 | "forget" (`DELETE /api/chat/sessions/{id}`) → 200, but **the kagent session stays** | different, **by design** | `ForgetSessionCommand`: "drops the session from the index (not from kagent)"; the run deleted them in kagent |
| Memory/knowledge | create/update | `/api/memories` → 500 "relation memory does not exist"; no UI (`/memories` 404) | cards: PUT v0 → 201, v1 → 200, stale v1 → 409 "Stale version 1." | different | kagent long-term memory is not set up here; company knowledge is Skanyxx cards (D016) |
| Memory/knowledge | read/delete | — | GET 200; delete → 405 (no delete route; versions are kept) | by design | |

## Tests

Full suite, `dotnet test Skanyxx.sln -p:UseSharedCompilation=false`, run once after all fixes: **2326/2326 passed** (6 test projects, 0 failed, 0 skipped).
