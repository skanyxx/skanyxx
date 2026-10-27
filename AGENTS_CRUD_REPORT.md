# Agents CRUD — report (branch `agents-crud`)

requirements.md §3.3 / O6: full agent CRUD like kagent-ui, role-gated, through kagent's controller HTTP API.
Decisions: D250–D256 in `docs/design/decisions.md`. Open items: `ISSUES.md`.

## Endpoints (`src/Modules/Skanyxx.Module.Agents/Manage/Endpoints`)

| Method | Route | Roles (endpoint gate) | Answers |
|---|---|---|---|
| GET | `/api/agents` | owner, supervisor, builder, employee | 200 list (employees: agents in Chat only) |
| GET | `/api/agents/{ns}/{name}` | owner, supervisor, builder, employee | 200 detail; 404 also when the caller may not read it; system prompt only for builder+ |
| GET | `/api/agents/options` | owner, supervisor | models, allow-listed MCP servers + tools, callable agents, skill registries |
| POST | `/api/agents` | owner, supervisor | 201; 400 form problems; 409 name taken |
| PUT | `/api/agents/{ns}/{name}` | owner, supervisor | 200; 400; 404; 409 studio/system agent, other namespace, BYO, prompt from ConfigMap/Secret, foreign tools |
| DELETE | `/api/agents/{ns}/{name}` | owner, supervisor | 200; 404; 409 studio/system agent, other namespace |

Controller calls (`KAgentApiClient`): `GET /api/agents`, `GET/DELETE /api/agents/{ns}/{name}`, `POST/PUT /api/agents`,
`GET /api/modelconfigs`, `GET /api/toolservers`. Always as Skanyxx's own process user; no person's id or request
header is forwarded. Every check runs before the first controller call (kagent 0.10.2 is `unsecure`).
The old MVC `AgentsController` / `IAgentService` / `KAgentApiClient.GetAgentAsync` are removed.

## Role matrix (D254, also in `docs/design/roles.md`)

| Action | Owner | Supervisor | Builder | Employee |
|---|---|---|---|---|
| List | all | all | all | agents in Chat |
| Details | full | full | full, read-only | agents in Chat, no system prompt |
| Options / create / edit / delete | yes¹ | yes¹ | 403 (propose in Studio) | 403 |

¹ Never studio agents (409, points to `agents/<name>/agent.yaml` and Studio), never system agents (seed,
skanyxx-factory, skanyxx-*, preview-*, ticket-flow stages), only in `Studio:Namespace`.

## What the form may set (D251, D256)

Declarative only: description, system prompt (≤16k), model (ModelConfig in the namespace), runtime, stream, MCP
servers from `Studio:McpServers` with named tools (never a memory server), agent tools (direct/external agents only),
skills digest-pinned from `Studio:SkillRegistries`, A2A card skills. Names DNS-1035 ≤ 63, not reserved; no control or
format characters. Never BYO/deployment/env/headers/prompt sources. Edits start from the current spec and change only
these fields (kept: deployment, memory, a tool's `headersFrom`/`isolateSessions`/`allowedHeaders`); what an agent
already has may stay, never widen. New agents are labelled `skanyxx.dev/managed-by: skanyxx-agents` and
`skanyxx.dev/merged: "true"` (in Chat, D253).

## Audit (D255)

`RecordAuditCommand` (Core) → identity audit: `agent.created`, `agent.updated`, `agent.deleted`,
`agent.write_refused`; builder/employee writes are `route.owner_refused` (sampled). Names only, never prompt text.

## UI

`/Agents`: table (name, namespace, source, status, in Chat), details panel, create/edit form (MCP tools per server,
agent tools, skills, A2A skills), delete with confirm. Write buttons render only for supervisor/owner (cosmetic).
All API values set with `textContent` (the old page used `innerHTML`).

## Files

- Core: `Platform/Identity/RecordAuditCommand.cs` (new), `Services/KAgentApiClient.cs` (dead `GetAgentAsync` removed),
  `Interfaces/IAgentService.cs` (removed).
- Identity: `Features/Audit/RecordAuditHandler.cs` (new).
- Agents module: `Manage/**` (new: definitions, handlers, endpoints), `AgentsModule.cs`, `Studio/Definition/DraftRules.cs`
  (two regexes internal), removed `Controllers/AgentsController.cs`, `Services/KAgentAgentService.cs`.
- Host: `Pages/Agents.cshtml(.cs)`.
- Tests: `tests/Skanyxx.Host.Tests/AgentsApiTests.cs` (new), `StudioTests.cs` (read-only test → refusal test).
- Docs: `decisions.md` (D250–D256), `roles.md`, `studio.md`, `cluster-security.md`.

## Tests

- `dotnet build Skanyxx.sln -c Release -p:UseSharedCompilation=false`: 0 errors, 0 warnings.
- `dotnet test --solution Skanyxx.sln -c Release`: **2215 total, 2215 passed, 0 failed, 0 skipped** (Host 26 min).
- `AgentsApiTests` + `StudioTests`: 76/76. RBAC: anonymous 401 on all 7 routes; builder and employee 403 on every
  write and on options, with zero kagent write calls; employees never see unmerged agents or system prompts.
- Build note: parallel sessions share the compiler server, and a stale one fails untouched Razor pages; build with
  `-p:UseSharedCompilation=false` (ISSUES.md A7).

## Live check (kagent controller `localhost:8083`, Host on 127.0.0.1:5199, throwaway Postgres)

Run 1 (`crud-test-123902`, before D256): 20/20. Run 2 after the review fixes (`crud-test-160646`): **22/22 passed** —
options; employee create 403; memory server 400; foreign namespace 400; `seed` as an agent tool 400; create 201 with
both labels in kagent; read (owner full, employee without prompt); update 200 with the spec changed in the controller
and labels kept; delete outside `Studio:Namespace` 409; employee delete 403; owner delete/edit of `seed` and delete of
`skanyxx-factory` 409; delete 200; gone from the controller; Skanyxx 404; `seed`/`skanyxx-factory` resourceVersion
unchanged (706/795); audit rows created/updated/deleted/write_refused. Page (run 1): owner sees "New agent" and the
form, employee does not. Afterwards kagent listed only `seed` and `skanyxx-factory`; the Host and its Postgres
container were removed.

## Left (see ISSUES.md OPEN)

Lost-update protection, 409 for a racing create, list-level edit hints, studio write-back as PR, enable/suspend and
unpublish, name collisions with later studio merges, D-number coordination at merge.
