# AX integration (v1 Tasks pack, as built)

`ax-tasks.md` says **what** (Tasks = AX). This file says what AX v0.3.1 **really is** and how the v1 slice fits it.
Source: primary-source read of `github.com/google/ax` (main @ `c5c1ac5`, 2026-09-25); the proto and every v0.3.1 claim are pinned to tag **v0.3.1 @ `e70162a`**. Nothing below was run on a real cluster.

## 1. What AX v0.3.1 is

- **Rewritten 2026-09-19** (v0.3.0, `dc4f36c`). Earlier AX (Interactions API, A2A examples) is gone. README: breaking changes expected.
- A **sandbox orchestrator**, not an agent runtime. Task = "run this image + command in a Substrate sandbox with these workspaces". No prompt, session, result, log or artifact API.
- API = one gRPC service `ax.v1alpha1.AX` on `ax-server:8080`, **h2c, no TLS, no auth** (open issue #376). No REST, no reflection, no CRDs.
- State in **Redis** (keys `ax:*`, stream `ax:stream:tasks`), not etcd. Tenancy = **atespace**.
- Lifecycle: `Pending → Running | Suspended | Failed | Terminating`. `Completed` is **never set**; command exit status is **not reported**.
- `WatchTask` closes once phase is `Running`/`Failed`. After that: poll `GetTask`. Pub/Sub misses are lost.
- Stop = `DeleteTask` (async, two-phase). Suspend/resume snapshot memory + `/workspace` (bucket default is a dev's personal GCS — must override).
- Needs **Agent Substrate** underneath (Postgres, object store, gVisor/microVM, beta k8s cert APIs). Neither repo ships a **Helm chart** (kustomize + `ko`).

## 2. Design vs AX reality

| Design says | AX v0.3.1 does | Consequence |
|---|---|---|
| D064: attach **our memory MCP** to a Workspace | `mcp.servers[]` has name/endpoint only, **no headers**. Default runner **does not write MCP config** at all. | Declaring it is not enough. The task image must read `$AX_METADATA_URL/…/workspaces` + env ids and set headers itself. |
| "Run / watch / stop" | Watch covers start-up only. No done signal, no logs. | UI "watch" = SSE until `Running`, then poll. "Done" must come from the agent (or never). |
| D067: AX + Substrate **in the umbrella, on by default** | No Helm chart; Substrate needs GKE 1.36+/k8s 1.37 beta APIs, sandbox runtime, object store. Substrate calls itself aspirational. | We would author and own both charts. Appliance (hidden k3s VM) likely cannot meet Substrate prereqs. |
| D068: one **Dragonfly** as AX Redis | Commands used are Dragonfly-compatible; **no password/TLS/ACL/DB index** options beyond `--redis-password`. | Plausible, **untested**. Shared box shares one password with memory cache; NetworkPolicy does the rest. |
| v1: "list **Models**" | AX `Model` = config for AX's **own** planner, Gemini only; not given to tasks; unrelated to kagent `ModelConfig`. | Show read-only, labeled. Do not map kagent models onto it. Never put keys in `spec.env`: plaintext in ActorTemplate, served by task metadata. |
| D066: small **port** (Agents vs Tasks) | kagent side has no port today (`KAgentApiClient` used directly in 4 controllers). | The AX module is a pass-through, not the port. The port stays a later refactor. |

## 3. Duplicate / reuse

| AX need | Skanyxx already has | Decision |
|---|---|---|
| Run record, retries, gates | **Tickets** = durable run engine over kagent A2A (runs, per-user cap, cancel, gates) | **Avoid.** No second run engine. AX is source of truth; no local run table. |
| Word "Task" | kagent A2A tasks (`KAgentTask`) in Sessions/Analytics/Dashboard (all removed in Phase 1); Tickets "runs" | **Avoid** the name in code/routes. Module + routes say **Sandboxes**; UI label may say "Tasks (Experimental)". |
| Per-user cap, who may stop | Tickets rule: cap per user, creator-or-supervisor cancel | **Reuse the rule**, not the code: same semantics in Sandboxes. |
| Memory for the sandbox | Memory MCP `/mcp/memory` (agent = owner of the presented per-agent secret, D080/D083; `X-User-Id` only for agents the owner lets act for users, D084) + `PUT /api/memory/grants/{agentId}` | **Reuse.** Attach helper = workspace MCP entry + env ids; grants stay a supervisor call. Attach still needs a per-task credential (D076/D077): no sandbox may hold an agent secret. |
| Identity of a sandbox | Agent ids in memory grants | **Reuse** the agent-id space: a sandbox gets a synthetic agent id for grants. |
| Redis | Dragonfly (D068) | **Reuse** if the smoke test passes. |
| Models | kagent `ModelConfig` | **Avoid** coupling; separate concepts. |
| Streaming to UI | Nothing reusable: no server-side SSE in Chat (only the kagent client's Accept header) | **Own** SSE relay in Sandboxes (`WatchTaskEndpoint`). |

### Duplicates already inside Skanyxx

Found by the survey; not caused by AX. Candidates for a later cleanup.

| Duplicate | Where | Later |
|---|---|---|
| Two kagent A2A clients | Core `KAgentApiClient` vs Tickets `KAgentStageClient` (different user-id behaviour) | One client |
| Three `Supervisors` lists | Memory, Tickets, Sandboxes options | **Done (identity slice 1):** lists removed; supervisor = role `owner`/`supervisor` from the signed-in user |
| Creator-or-supervisor + per-user cap | Tickets, Sandboxes | Shared policy |
| KAgent* wrappers | 6 modules; `KAgentApiClient` used directly in 4 controllers | Runtime port (D066) |
| Investigate | One-shot, in-memory copy of what Tickets does durably | Fold into Tickets |
| ToolServers create/delete | Controller and service | Keep one |
| Debug | Shows synthetic logs; calls a missing route | Fix or drop |

## 4. v1 slice as built (W2)

- Module `Skanyxx.Module.Sandboxes`, routes under `api/sandboxes`, sandbox noun throughout (D071; route shape D179):
  `GET /api/sandboxes?limit=&offset=` (list), `GET /api/sandboxes/{name}`, `PUT /api/sandboxes/{name}` (run),
  `POST /api/sandboxes/{name}/stop|suspend|resume`, `GET /api/sandboxes/{name}/watch` (SSE), `GET /api/sandboxes/workspaces`,
  `PUT /api/sandboxes/workspaces/{name}`, `GET /api/sandboxes/models`. `workspaces` and `models` are reserved sandbox names
  (refused on run): literal routes win over `{name}`. The pre-D179 `/api/sandboxes/tasks/*` routes are gone (`404`).
- gRPC pass-through from `ax.proto` pinned to **v0.3.1**. Labeled **Experimental** (D064). One sandbox = one AX `Task`;
  only AX's generated types keep AX's name (an architecture test fails on any other module type named `*Task*`).
- List: sandboxes, workspaces, models. Get one sandbox.
- Run = `UpdateTask`. Watch = `WatchTask` → SSE, then poll `GetTask`. Stop = `DeleteTask`. Suspend (Pending/Running only) / resume (Suspended only), else `409`: AX's reconciler ignores the phase, so suspend/resume on a Failed/Terminating sandbox would reactivate it past the caps.
- Memory attach helper: workspace MCP entry → the sandbox listener's `/mcp/memory`; sandbox env `SKANYXX_AGENT_ID=ax-<name>-<nonce>` (fresh each time the sandbox becomes active; the owner replacing an active sandbox keeps it) / `SKANYXX_USER_ID` (informational). **D076/D077 built (2026-10-11, D190–D194):** `Memory:SandboxMcpPort` serves `/mcp/memory` only, identifying every request by a Skanyxx-signed token (`skx_sbx_…`, HMAC-SHA256 with `Memory:SandboxTokenKey`, binding sandbox, agent id, owner, expiry) and ignoring headers; a run that binds a memory-attached workspace gets `SKANYXX_MEMORY_URL` + `SKANYXX_MEMORY_TOKEN`; the image sends `Authorization: Bearer $SKANYXX_MEMORY_TOKEN` (D073 contract). One live token per activation (Postgres row; stop / re-run / owner offboarding revoke it at once; expiry `SandboxTokenHours`, default 24, renewed when the sandbox is resumed). The sandbox acts for its owner only. Sandboxes never writes grants; a supervisor calls `PUT /api/memory/grants/<agent id>`.
- Ownership: creator stored in sandbox env `SKANYXX_OWNER` only (v0.3.1 has no labels). Run/replace = creator; stop/suspend/resume = creator or supervisor.
- Limits: `AllowedImages` (+ optional digest; `:` is a prefix boundary only when it starts a tag, never a registry port), `MaxCpu`/`MaxMemory`, global + per-user cap (re-runs count), watch caps. Condition messages not returned.
- Cap check: every activating run counts + upserts under one atespace-wide lock (per replica), so distinct `X-User-Id`s cannot race past the global cap. The count pages the **whole atespace** each time (v0.3.1 `ListTasks` has no owner/label filter): O(tasks) per activation, activations serialised, bounded by `CountBudgetSeconds`, refused at 10k tasks. Paging stops only on an empty page (AX drops unparsable entries, so a short page is not the end). Still approximate: AX orders by last save, so concurrent saves can shift offsets.
- Startup safety (implements the D072/D077 intent; D069–D077 locked 2026-10-08): the module is **off by default** (enable it explicitly), a `MemoryMcpUrl` is **refused at startup** unless it is the sandbox listener's `/mcp/memory` with the signing key set (D193; before 2026-10-11 every value was refused), and a non-empty `AllowedImages` is refused unless `Sandboxes:NetworkIsolationConfirmed=true`. NetworkPolicy manifests: `deploy/sandboxes/` — the deny policy verified on kind + gVisor in the WorkerPool's namespace (D178); micro-VM / GKE unverified.
- AX has no auth → **Skanyxx is AX's only client**. NetworkPolicy: only the Skanyxx API pod reaches `ax-server:8080`. No browser/desktop client talks to AX directly.
- **In the umbrella chart (2026-10-11, D186–D189):** `ax.install` (default false, D069) installs AX v0.3.1's `ax-server` + `ax-controller` in the release namespace on the bundled Dragonfly (D074 smoke test passed: D185), points `Sandboxes:Address` at it, fences `ax-server` to Skanyxx (D072) and renders the sandbox egress policies in the WorkerPool's namespace (D178, D188). Images and `AX_SNAPSHOTS_BUCKET` are values (D187); with Dragonfly off AX uses `external.redis` or its own Redis (D189). The chart cannot create Substrate, the WorkerPool or AX's base ActorTemplate (Substrate API objects): the operator does. Shown on kind-ate; README "Install AX with the chart".

## 5. AX decisions (proposed 2026-09-25, **locked 2026-10-08** — the rows now live in `decisions.md`)

| ID | Proposed decision | Why |
|---|---|---|
| D069 | **Supersedes D067's default for the appliance.** AX + Substrate **off** in the appliance; on-by-flag for BYO clusters that meet Substrate prereqs. | No upstream chart; Substrate needs beta k8s APIs + sandbox runtime a hidden k3s VM may not have. |
| D070 | AX is **source of truth** for sandboxes. Skanyxx keeps **no run table** and builds **no second run engine**. Tickets may later *call* Sandboxes as a step. | Two run engines drift. Tickets already owns durability for kagent. |
| D071 | Code and routes say **Sandboxes**, never "Task". | "Task" already means kagent A2A tasks and is near Tickets runs. |
| D072 | ax-server is reachable **only** from the Skanyxx API (NetworkPolicy). Skanyxx enforces owner, cap, creator-or-supervisor stop. | AX has no auth (#376). |
| D073 | Memory attach is a **contract with the task image**: read workspace MCP entry + `SKANYXX_*` env, send headers. Default runner is not supported for memory. | AX has no MCP headers and does not materialize MCP config. |
| D074 | D068 holds **only after** a Dragonfly smoke test of AX (streams, MULTI, Pub/Sub). Until then, AX may use its own bundled Redis. **Passed 2026-10-09 for v0.3.1 on Dragonfly v2.0.0 (D185).** | Compatibility inferred from code, not tested. |
| D075 | Pin AX to one version (**v0.3.1** proto). Upgrades are a deliberate bump, not a chart float. | API was replaced 6 days before we read it. |
| D076 | Memory identity for sandboxes comes from a **Skanyxx-signed per-task token** (HMAC in reserved env), not headers. On the sandbox-facing listener (D077) **every** request is identified only by that token; headers are ignored and a request without a valid token is rejected, whatever agent id it names. **Built 2026-10-11 (D190–D192).** | A sandbox picks its own `X-Agent-Id` / `X-User-Id` today; a rule scoped to `ax-*` ids is bypassed by sending a non-`ax-` id. Threat model includes workspace writers (supervisors), whose git/MCP content runs under other owners' identities. |
| D077 | Sandbox egress is **default-deny** (internet = `0.0.0.0/0` minus cluster CIDRs, RFC1918, `169.254.0.0/16`). A **dedicated sandbox-facing listener** (own port/Service) serves `/mcp/memory` and nothing else (`RequireHost` on the MCP map + every other endpoint kept off that port); it is the only Skanyxx port sandboxes may reach. **Not implemented**: today `/mcp/memory` is on the main pipeline, so until then `MemoryMcpUrl` stays empty and sandboxes reach no Skanyxx port. **Built 2026-10-11 (D190–D194):** `Memory:SandboxMcpPort` (local-port match, not `RequireHost`, as D138), chart `ax.sandboxes.memory`. | NetworkPolicy filters ports, not paths; a second port on today's pipeline would serve the whole API. |

## 6. Open questions for the doc owner

- ~~Accept D069 (AX off in appliance) or keep D067 and own a Substrate chart?~~ D069 locked 2026-10-08.
- What counts as "done" for a sandbox? Agent upserts a card / calls back, or "done" is not a v1 concept?
- ~~Accept D076 + D077 (token-only sandbox listener) as the precondition for turning memory attach on?~~ Locked 2026-10-08.
- Does a Kubernetes NetworkPolicy apply to Substrate sandboxes (gVisor/microVM) at all? **gVisor on kind: yes, on the WorkerPool's namespace (D178, 2026-10-08).** Micro-VM / GKE: unverified.
- Which task image do we ship that honors the memory contract (D073)? Ours, or customer-provided only?
- Atespace per install, per team, or per user?
- Do Tickets ever launch sandboxes in v1, or strictly later?
- Dedicated, quota-capped Gemini key / atespace without the platform key: who owns it?
- Who may widen `AllowedImages` (config owner only, or supervisors)?
