# Build todo (follow this order)

For people. AI agents: read [`AGENTS.md`](../../AGENTS.md) first, then this file. Do **not** skip ahead to Helm, studio, or AX until the current slice’s “done when” is true.

Locked product: `spine.md`, `three-planes.md`, `first-hour.md`, `decisions.md`. Community catalog is **paused** (D054).

## Status of `dotnetapi` (2026-10-05)

**Shipped as slices (keep; do not rewrite):** identity (owner, invite, roles, org, Entra), memory engine (cards, lift, grants, MCP + agent secrets), tickets (kagent stage runs), sandboxes module (AX v0.3.1, experimental, **off**).

**Not the product yet:** ~~first-hour still stops at `/Setup`. No kagent, no seed chat,~~ (2026-10-05: slice 1 done — `/Setup` → `/Model` → Chat with the seed) ~~no library page,~~ (2026-10-05: slice 2 done — `/Library`) ~~no studio/PR,~~ (2026-10-06: slice 3 done — `/Studio`, PRs in `skanyxx-agents`) no Helm umbrella. Nav is still the SRE console (Dashboard, Investigate, Alerts, Hooks, Cloud Tools).

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

- [x] Document and script a **local three-plane run**: Postgres (`docker compose`) + Host (`dotnet run`) + kagent (kind/k3d or existing cluster) pointed at `KAgent` in config.
- [x] Apply **only** `deploy/kagent/memory/` (seed Agent + RemoteMCPServer). Do **not** ship kagent’s sample pack (D019).
- [x] Issue the seed’s memory secret (`POST /api/memory/agents/{id}/secret`, owner, `actsForUsers: true` as designed) and put it in the k8s Secret the RemoteMCPServer uses.
- [x] After Setup, land on **Chat with the seed**, not Dashboard. Model-key / `ModelConfig` belongs in this hour (owner job in `roles.md`).
- [x] Chat lists **merged** agents only (today: seed). kagent dashboard is not the talk surface (D017).

> **2026-10-05 — slice 1 done** (branch `slice1-first-hour`). `scripts/dev/first-hour.sh up|host|seed-secret|status`
> and README "First hour"; owner-only model step `/Model` / `PUT /api/model` writes kagent's ModelConfig through kagent's
> API (D099); merged = label `skanyxx.dev/merged: "true"` (D100); `/` → Chat, Chat acts as the signed-in person (D101).
> Shown on kind + kagent 0.10.2 + Ollama `qwen3-coder:30b`: fresh DB → /Setup → /Model → Chat → the seed's
> `memory_search` found a company card and the answer cited it (task `2026-10-05_1700_skanyxx-first-hour`,
> evidence/e2e.md).

## 2. Library (humans can see the bank)

Design: `memory-card.md`, `memory-hierarchy.md`, `memory-how.md`, D009, D038, D048.

**Done when:** a signed-in person can search and open **published `company`** cards in Skanyxx, and lift a card they are allowed to lift. No folder tree.

- [x] Library page (or equivalent) over existing `/api/memory/cards`. API already exists — this is UI + grants/lift affordances, not a new store.
- [x] Human may rename `key` (D038). Agents still propose keys.
- [ ] Bodies/blobs: Postgres card first; MinIO pointers (`source`) when a body is more than the card. Do not inline blobs into prompts (D014).

**2026-10-05 — slice 2 built (D102).** `/Library` (in everyone's nav): search + scope filter + card view (body and
source shown as text) + lift buttons only for higher scopes the person may write + rename key where they may write
(`POST /api/memory/cards/{scope}/{key}/rename`). All rules are memory's `AccessPolicy`, reached through
`Skanyxx.Core.Platform.Memory` commands; no new store. Bodies live on the Postgres card; `source` is a pointer shown
as text and never sent to agents (D014). **Open:** MinIO/S3 pointers for bodies larger than the card (third box stays
open), display names for `who`. Done-when verified on a real host (task evidence `e2e.md`). The status line above
("no library page") predates this: the library page is built (slice 2). Unpublished `company` cards open only for
supervisors and their author (D103); QA round-1 refinements in D104.

## 3. Studio + factory + git PR

Design: `studio.md`, `seed-vs-factory.md`, D020–D024, D028–D033, D045.

**Done when:** a builder composes an agent in a form (or factory chat), that opens a PR in `skanyxx-agents`, and only a **supervisor** merge makes it live in kagent. Preview chat must not upsert production cards (D032).

- [x] Setup **creates** the agent-config repo (D023). Customer git is an owner option, not a blocker.
- [x] Studio form fields = D029 (name, model, instructions, skills, MCP, **search/upsert grants**, optional kagent TTL). Grants live in Skanyxx, YAML in git, **same PR**.
- [x] Factory is builder-only sugar over the same files (D022). Employees never get factory tools.
- [x] Our reconciler applies merged YAML to kagent (D045). Not live `kubectl apply` from the form.
- [x] Employees see merged agents only (D031). Preview is builder/supervisor.

**2026-10-06 — slice 3 built (D109–D115).** `/Studio` (builders, supervisors, owner) + `api/studio`: the form or the
factory's draft → one PR in `skanyxx-agents` (bundled Gitea, created at setup, `main` protected) carrying `agent.yaml` +
`grants.yaml`; preview `preview-<pr>-<name>` (search on company only, never upsert, not in Chat); supervisor merge (owner
for team/department grants), files re-checked at the pinned head; the in-process reconciler applies main through kagent's
API with the merged label and a studio-issued memory secret. Legacy `/api/agents` writes are owner-only. Shown on kind +
kagent 0.10.2 + Gitea 1.24 + Ollama: builder 403 on merge, preview could not write a card, supervisor merge → live →
employee chat answered from memory (task `2026-10-06_0900_skanyxx-studio`, evidence/e2e.md). `scripts/dev/first-hour.sh git`.

**2026-10-06 — slice 3 QA round 1 (D116–D123).** Studio hardened: untrusted YAML scanned and size-capped; the memory
secret kagent holds tracked by fingerprint; one reconcile at a time across replicas, a failing pass never stops the Host;
only memory principals the studio claimed; the repo recorded and never re-created, plus a removal brake the owner lifts;
skills digest-pinned from allow-listed registries, never in previews; the owner's suspend/resume; leftover routes that
change kagent or the cluster are owner-only.

## 4. Ship the stack (Helm umbrella)

Design: `install-path.md`, D056–D061, D067–D068.

**Done when:** one chart installs Skanyxx API + kagent + memory + Postgres + MinIO + git + one Dragonfly. Browser URL is the product. Local fat Host is no longer the source of truth.

- [x] Umbrella chart; values turn kagent / AX / bundled stores off and point at BYO (D061).
- [ ] Appliance = that chart on hidden k3s (they never `kubectl`). BYO = same chart on their cluster.
- [ ] Thin today’s Host toward a **client** of the in-cluster API (D060, D059). Company users/org/cards must not live only in `%APPDATA%` or a laptop SQLite.
- [ ] AX **on by default in the chart** is D067; appliance default-off is D069 (locked 2026-10-08, so the appliance path is unblocked). Do not pretend Substrate fits a tiny k3s VM.

**2026-10-06 — slice 4 (branch `slice4-helm`): the umbrella chart is built (D130–D135).** `deploy/helm/skanyxx`:
Skanyxx (new `Dockerfile`, `scripts/helm/build-image.sh`) + kagent 0.10.2 + Postgres + MinIO + Gitea + one Dragonfly;
each bundled piece has `enabled` and BYO settings; `scripts/helm/validate.sh` (lint + kubeconform) for default, BYO and
AX values; `scripts/helm/seed-agent.sh` for the seed after `/Setup`; README "Install with Helm". Shown on kind:
install → pods Ready → `/health` 200 → owner bootstrap → stores reachable from Skanyxx → `/Model` creates
`default-model-config` (the chart never renders it, D131) → seed tools discovered → `helm upgrade` with the same values
changes nothing (manifest, Secrets, resourceVersions, pods, the owner's ModelConfig) → uninstall (task
`2026-10-06_0900_skanyxx-helm`, evidence/e2e.md). **Box 2:** BYO = this chart on their cluster (done); the appliance
(hidden k3s) is **not built** — waits on D069. **Box 3:** the in-cluster Skanyxx is the source of truth for users, org
and cards; the Host itself is unchanged (no thin client yet, D134). **Box 4:** `ax.enabled` exists, default **false**,
and installs no AX (points the sandboxes module at one you run) until D067/D069 is locked (D134). MinIO's own images
are gone; the default image is the `pgsty/minio` fork (D132).
QA round 1 (same day, D136–D146): kagent's RBAC namespaced (one bundled-kagent install per **cluster**); the
namespace default-deny with kagent's agent pods covered; `/mcp/memory` on its own port (`Memory:McpPort`, a Host
change with tests); the Host exits 1 on a startup failure (it used to spin as PID 1); Secrets kept on uninstall +
`existingSecret` for every store (GitOps); digests, `values.schema.json`, Postgres startup probe, a product-only
ingress list. Evidence `evidence/e2e-qa1.md`.

## 5. AX Tasks (experimental, after Agents path works)

Design: `ax-tasks.md`, `ax-integration.md`, D063–D064, D069–D077 (locked 2026-10-08).

**Done when:** a supervisor can list/run/watch/stop a sandbox against a real AX, labeled Experimental. No Task-studio, no Task preview-chat.

- [x] Lock or drop proposed D069–D077 in `decisions.md` before more AX product work. **2026-10-08:** all nine locked as written.
- [ ] Dedicated sandbox-facing `/mcp/memory` listener + per-task token (D076/D077). Do **not** hand an agent secret to a sandbox.
- [ ] Code/routes stay **Sandboxes**, never “Task” (name clash with kagent A2A and Tickets).
- [ ] No second run engine. AX is source of truth for sandboxes; Tickets stays kagent.

**2026-10-08/09 — a real AX and the page.** D069–D077 locked. Real AX v0.3.1 on Substrate (kind, arm64, gVisor) ran
list / run / watch / stop / suspend through `/api/sandboxes` (resume fails for image-named tasks: AX v0.3.1 limit;
README "Local AX on kind"). Sandbox egress-deny verified on the WorkerPool's namespace (D178). `/Sandboxes` page (owner +
supervisor, Experimental) clicked through in a real browser against that AX: run → live Pending/Running → stop, and an
API refusal shown. **Done-when not yet fully met:** only the owner was exercised against the real AX (a supervisor
acting on another person's task: module tests against the fake only); D076/D077 listener open; D071 rename open
(routes and ~25 types still say "Task").

## 6. Later (do not start while 1–3 are open)

- [x] Identity leftovers in `open.md` (email delivery, durable audit, password reset, Graph re-check, sandbox stop on `PrivilegesRevoked`, …).
- [x] Polly → `Microsoft.Extensions.Http.Resilience`; xunit v3 (split out, next bullet); delete or adopt root `SkanyxxWeb.csproj`; untrack `bin/`/`obj/`; add tests to the release workflow (`open.md` Platform). **2026-10-07:** done — Polly removed with the uncalled `AddResilientHttpClient`, no client retries (D170); legacy root monolith deleted (D171); `bin/`/`obj/` untracked (D172); release `test` job gates every installer (D173).
- [x] xunit v3 (follow-up wave, not part of the 2026-10-07 platform change). **2026-10-07:** done — all five suites on `xunit.v3` 4.0.1 under Microsoft.Testing.Platform (`global.json` `test.runner`), ArchUnitNET on its xUnitV3 package; counts unchanged (Host 362, Identity 395, Memory 319, Sandboxes 229, Tickets 311) (D175).
- [x] Host tests on Windows: parallel testhosts lock `skanyxx.db` on dispose. Fix the harness (unique content-root / serial collection), do not delete the tests. **2026-10-07:** content roots were already unique; the lock was the SQLite connection pool — cleared before the delete, test added (D174). Unconfirmed on Windows.
- Community templates (D005 / D054) — paused.

**2026-10-07 — section 6, identity leftovers built (D150–D159; branch `slice6-identity`).** SMTP email through MailKit
(`Identity:Smtp`; off = behaviour as before): invite links emailed to the invitee (D151). Durable `identity_audit`
written in the transaction of every audited change, append-only, owner-only Audit page/API, retention job that also
purges finished invites (D152, D155). Password reset: "Forgot your password?" (same answer for every email, link from a
background queue, single-use, hashed, 60 min) and an owner-issued link from People; never for the owner, a disabled
account or an Entra-managed one while Microsoft sign-in is on (D156, D157). Entra Graph re-check every 60 min re-maps or
refuses managed accounts between sign-ins (D158). A disable stops the person's sandbox tasks, and every
`PrivilegesRevoked` handler runs even when another fails (D153, D154). Left over: `open.md` "Identity — left over from
section 6" (owner password recovery, in-process reset queue, guests not re-checked, …). The other section-6 bullets
(Polly, root project, `bin/`/`obj/`, release tests, Windows harness) were done the same day by the platform change (D170–D174); xunit v3 followed the same day (D175).

## Known local-dev traps (not product work)

- Host `appsettings.json` is gitignored and **older than** `appsettings.template.json`. Run with template values via env (`ConnectionStrings__Memory` / `Identity` / `Tickets`) or copy the template locally. Do not commit secrets.
- Postgres: `docker compose up -d` → `127.0.0.1:55432`.
- Windows Application Control / Smart App Control can block `ArchUnitNET.xUnitV3.dll` (and sometimes module DLLs). That is an environment failure, not a reason to drop architecture tests.
- `/mcp` must not be on the public ingress. kagent → memory is cluster/mesh or HTTPS.
