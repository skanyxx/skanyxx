# Build todo (follow this order)

For people. AI agents: read [`AGENTS.md`](../../AGENTS.md) first, then this file. Do **not** skip ahead to Helm, studio, or AX until the current slice’s “done when” is true.

Locked product: `spine.md`, `three-planes.md`, `first-hour.md`, `decisions.md`. Community catalog is **paused** (D054).

## Status of `dotnetapi` (2026-10-05)

**Shipped as slices (keep; do not rewrite):** identity (owner, invite, roles, org, Entra), memory engine (cards, lift, grants, MCP + agent secrets), tickets (kagent stage runs), sandboxes module (AX v0.3.1, experimental, **off**).

**Not the product yet:** first-hour still stops at `/Setup`. No kagent, no seed chat, no library page, no studio/PR, no Helm umbrella. Nav is still the SRE console (Dashboard, Investigate, Alerts, Hooks, Cloud Tools).

## How to take a slice

1. Read the linked design file and the D-numbers in the row.
2. Extend the named module. Do not add a sibling engine.
3. Tests in `tests/Skanyxx.Module.<Name>.Tests` (and Host tests if the page lives in Host).
4. If you reverse a decision, add a **new** D-row. Never edit an old row in place.

---

## 0. Guardrails (always)

- [ ] Do not grow SRE/NOC features (D001). Investigate / Alerts / Hooks / Dashboard / Cloud Tools / Analytics are leftover chrome. Fix only if they block the new path; otherwise leave or hide.
- [ ] Do not replace kagent with an in-process LLM loop (D002, D016). Chat talks to kagent.
- [ ] Do not treat kagent TTL memory or Dragonfly as the company bank (D008, D042, D068). Cards stay in Postgres.
- [ ] Do not wrap Joicy as the memory store (D046).
- [ ] Do not build “AX but chat like kagent” (D063). Sandboxes ≠ Agents.
- [ ] Tickets stays a kagent pipeline. Do not clone it for AX (see `ax-integration.md`).

## 1. First hour actually works

Design: `first-hour.md`, D017–D019, D025.

**Done when:** after `/Setup`, the owner pastes model credentials, opens Chat, talks to **one seed agent**, and gets an answer that can search memory.

- [ ] Document and script a **local three-plane run**: Postgres (`docker compose`) + Host (`dotnet run`) + kagent (kind/k3d or existing cluster) pointed at `KAgent` in config.
- [ ] Apply **only** `deploy/kagent/memory/` (seed Agent + RemoteMCPServer). Do **not** ship kagent’s sample pack (D019).
- [ ] Issue the seed’s memory secret (`POST /api/memory/agents/{id}/secret`, owner, `actsForUsers: true` as designed) and put it in the k8s Secret the RemoteMCPServer uses.
- [ ] After Setup, land on **Chat with the seed**, not Dashboard. Model-key / `ModelConfig` belongs in this hour (owner job in `roles.md`).
- [ ] Chat lists **merged** agents only (today: seed). kagent dashboard is not the talk surface (D017).

## 2. Library (humans can see the bank)

Design: `memory-card.md`, `memory-hierarchy.md`, `memory-how.md`, D009, D038, D048.

**Done when:** a signed-in person can search and open **published `company`** cards in Skanyxx, and lift a card they are allowed to lift. No folder tree.

- [ ] Library page (or equivalent) over existing `/api/memory/cards`. API already exists — this is UI + grants/lift affordances, not a new store.
- [ ] Human may rename `key` (D038). Agents still propose keys.
- [ ] Bodies/blobs: Postgres card first; MinIO pointers (`source`) when a body is more than the card. Do not inline blobs into prompts (D014).

## 3. Studio + factory + git PR

Design: `studio.md`, `seed-vs-factory.md`, D020–D024, D028–D033, D045.

**Done when:** a builder composes an agent in a form (or factory chat), that opens a PR in `skanyxx-agents`, and only a **supervisor** merge makes it live in kagent. Preview chat must not upsert production cards (D032).

- [ ] Setup **creates** the agent-config repo (D023). Customer git is an owner option, not a blocker.
- [ ] Studio form fields = D029 (name, model, instructions, skills, MCP, **search/upsert grants**, optional kagent TTL). Grants live in Skanyxx, YAML in git, **same PR**.
- [ ] Factory is builder-only sugar over the same files (D022). Employees never get factory tools.
- [ ] Our reconciler applies merged YAML to kagent (D045). Not live `kubectl apply` from the form.
- [ ] Employees see merged agents only (D031). Preview is builder/supervisor.

## 4. Ship the stack (Helm umbrella)

Design: `install-path.md`, D056–D061, D067–D068.

**Done when:** one chart installs Skanyxx API + kagent + memory + Postgres + MinIO + git + one Dragonfly. Browser URL is the product. Local fat Host is no longer the source of truth.

- [ ] Umbrella chart; values turn kagent / AX / bundled stores off and point at BYO (D061).
- [ ] Appliance = that chart on hidden k3s (they never `kubectl`). BYO = same chart on their cluster.
- [ ] Thin today’s Host toward a **client** of the in-cluster API (D060, D059). Company users/org/cards must not live only in `%APPDATA%` or a laptop SQLite.
- [ ] AX **on by default in the chart** is D067; appliance default-off is **proposed** D069 (`ax-integration.md`) — lock that before writing the appliance path. Do not pretend Substrate fits a tiny k3s VM.

## 5. AX Tasks (experimental, after Agents path works)

Design: `ax-tasks.md`, `ax-integration.md`, D063–D064. Proposed D069–D077 are **not** in `decisions.md` yet.

**Done when:** a supervisor can list/run/watch/stop a sandbox against a real AX, labeled Experimental. No Task-studio, no Task preview-chat.

- [ ] Lock or drop proposed D069–D077 in `decisions.md` before more AX product work.
- [ ] Dedicated sandbox-facing `/mcp/memory` listener + per-task token (D076/D077 proposed). Do **not** hand an agent secret to a sandbox.
- [ ] Code/routes stay **Sandboxes**, never “Task” (name clash with kagent A2A and Tickets).
- [ ] No second run engine. AX is source of truth for sandboxes; Tickets stays kagent.

## 6. Later (do not start while 1–3 are open)

- Identity leftovers in `open.md` (email delivery, durable audit, password reset, Graph re-check, sandbox stop on `PrivilegesRevoked`, …).
- Polly → `Microsoft.Extensions.Http.Resilience`; xunit v3; delete or adopt root `SkanyxxWeb.csproj`; untrack `bin/`/`obj/`; add tests to the release workflow (`open.md` Platform).
- Host tests on Windows: parallel testhosts lock `skanyxx.db` on dispose. Fix the harness (unique content-root / serial collection), do not delete the tests.
- Community templates (D005 / D054) — paused.

## Known local-dev traps (not product work)

- Host `appsettings.json` is gitignored and **older than** `appsettings.template.json`. Run with template values via env (`ConnectionStrings__Memory` / `Identity` / `Tickets`) or copy the template locally. Do not commit secrets.
- Postgres: `docker compose up -d` → `127.0.0.1:55432`.
- Windows Application Control / Smart App Control can block `ArchUnitNET.xUnit.dll` (and sometimes module DLLs). That is an environment failure, not a reason to drop architecture tests.
- `/mcp` must not be on the public ingress. kagent → memory is cluster/mesh or HTTPS.
