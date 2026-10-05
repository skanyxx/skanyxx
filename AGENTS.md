# Remarks for AI agents working in Skanyxx

You are implementing a **licensed company agent-management workspace**, not an SRE/NOC console. Read this before writing code.

**Work order:** [`docs/design/todo.md`](docs/design/todo.md). **Locked choices:** [`docs/design/decisions.md`](docs/design/decisions.md). **Walk:** [`docs/design/spine.md`](docs/design/spine.md).

If the human asks to **test or run only**, do not add, edit, or “clean up” product code.

## Product (do not invert)

| Skanyxx owns | kagent owns | Memory owns |
|---|---|---|
| People, roles, org, studio/PRs, library UI, grants | Running the model loop, MCP, skills, Agent CRs | Cards, search, upsert, lift, versions |

- Chat is **only** in Skanyxx and talks to **kagent** (D017, D002). Do not add an in-process chat runtime.
- AX is **Tasks / sandboxes**, not a second Agents chat (D063). Module and routes: `sandboxes`, never `task`.
- Company knowledge is **cards** in Postgres (`scope`+`key`+`version`), via `/mcp/memory`. Not transcripts, not Redis/Dragonfly, not Joicy, not kagent TTL memory.
- One customer = one install. We ship the stack (eventually Helm). Today’s fat local Host is a **dev console**, not the company source of truth (D060).

## Code shape

- Modules live under `src/Modules/Skanyxx.Module.*` and reference **only** `Skanyxx.Core`. They never reference Host or each other. Cross-module work = MediatR / Core interfaces (`IOrgMembership`, etc.).
- New module: `IModule` + copy DLL to `src/Skanyxx.Host/modules/` on build (see README). Declare `Dependencies`. Missing dep = fail startup, not a 500 later.
- Identity, Memory, Tickets, Sandboxes are **built**. Extend them. Do not start a second accounts DB, card store, run engine, or AX client.
- Prefer FastEndpoints in modules. Host Razor pages are for the current UI; do not grow leftover SRE pages (Dashboard, Investigate, Alerts, Hooks, Cloud Tools, Analytics).
- Secrets: never commit `appsettings.json` / real tokens. Template is `appsettings.template.json`. Local run: env `ConnectionStrings__Memory`, `__Identity`, `__Tickets` → Postgres `localhost:55432` (`docker compose`).

## Design log

- Reverse a decision → **new D-row** with date and why. Do not silently edit history.
- Community catalog is paused (D054). Do not build it.
- Proposed AX D069–D077 live in `docs/design/ax-integration.md` until they are copied into `decisions.md`. Do not treat them as locked.

## Tests

- `dotnet test Skanyxx.sln`. Memory/Identity/Tickets/Sandboxes tests need Docker (Testcontainers).
- Windows: Host tests can fail disposing `skanyxx.db` when collections run in parallel — harness bug, not “delete tests”. ArchUnitNET may fail with Application Control (`0x800711C7`) — environment, not architecture.
- Do not weaken security tests to make a slice green.

## Defaults that bite

- `sandboxes` is **off** unless enabled. `Sandboxes:MemoryMcpUrl` non-empty is refused until a sandbox-only MCP listener exists. Do not “fix” that by putting an agent secret in a sandbox env.
- Seed agent YAML: `deploy/kagent/memory/`. Do not apply kagent sample packs (D019).
- `/mcp/memory` = bearer agent secret; `X-User-Id` only if the owner issued `actsForUsers: true`.
