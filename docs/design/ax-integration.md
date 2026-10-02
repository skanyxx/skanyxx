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
| Word "Task" | kagent A2A tasks (`KAgentTask`) in Sessions/Analytics/Dashboard; Tickets "runs" | **Avoid** the name in code/routes. Module + routes say **Sandboxes**; UI label may say "Tasks (Experimental)". |
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

- Module `Skanyxx.Module.Sandboxes`, routes under `api/sandboxes`.
- gRPC pass-through from `ax.proto` pinned to **v0.3.1**. Labeled **Experimental** (D064).
- List: tasks, workspaces, models. Get one task.
- Run = `UpdateTask`. Watch = `WatchTask` → SSE, then poll `GetTask`. Stop = `DeleteTask`. Suspend (Pending/Running only) / resume (Suspended only), else `409`: AX's reconciler ignores the phase, so suspend/resume on a Failed/Terminating task would reactivate it past the caps.
- Memory attach helper: workspace MCP entry → `/mcp/memory`; task env `SKANYXX_AGENT_ID=ax-<name>-<nonce>` (fresh each time the task becomes active; the owner replacing an active task keeps it) / `SKANYXX_USER_ID`. Off in practice: `MemoryMcpUrl` stays empty until D077's listener exists. Sandboxes never writes grants; a supervisor calls `PUT /api/memory/grants/<agent id>`.
- Ownership: creator stored in task env `SKANYXX_OWNER` only (v0.3.1 has no labels). Run/replace = creator; stop/suspend/resume = creator or supervisor.
- Limits: `AllowedImages` (+ optional digest; `:` is a prefix boundary only when it starts a tag, never a registry port), `MaxCpu`/`MaxMemory`, global + per-user cap (re-runs count), watch caps. Condition messages not returned.
- Cap check: every activating run counts + upserts under one atespace-wide lock (per replica), so distinct `X-User-Id`s cannot race past the global cap. The count pages the **whole atespace** each time (v0.3.1 `ListTasks` has no owner/label filter): O(tasks) per activation, activations serialised, bounded by `CountBudgetSeconds`, refused at 10k tasks. Paging stops only on an empty page (AX drops unparsable entries, so a short page is not the end). Still approximate: AX orders by last save, so concurrent saves can shift offsets.
- Startup safety (implements the D072/D077 intent; D069–D077 stay **proposed**): the module is **off by default** (enable it explicitly), a non-empty `MemoryMcpUrl` is **refused at startup** (no longer only an operator rule), and a non-empty `AllowedImages` is refused unless `Sandboxes:NetworkIsolationConfirmed=true`. NetworkPolicy manifests: `deploy/sandboxes/` — still unverified on a Substrate sandbox runtime.
- AX has no auth → **Skanyxx is AX's only client**. NetworkPolicy: only the Skanyxx API pod reaches `ax-server:8080`. No browser/desktop client talks to AX directly.

## 5. Proposed decisions (not in `decisions.md` yet)

| ID | Proposed decision | Why |
|---|---|---|
| D069 (proposed) | **Supersedes D067's default for the appliance.** AX + Substrate **off** in the appliance; on-by-flag for BYO clusters that meet Substrate prereqs. | No upstream chart; Substrate needs beta k8s APIs + sandbox runtime a hidden k3s VM may not have. |
| D070 (proposed) | AX is **source of truth** for sandboxes. Skanyxx keeps **no run table** and builds **no second run engine**. Tickets may later *call* Sandboxes as a step. | Two run engines drift. Tickets already owns durability for kagent. |
| D071 (proposed) | Code and routes say **Sandboxes**, never "Task". | "Task" already means kagent A2A tasks and is near Tickets runs. |
| D072 (proposed) | ax-server is reachable **only** from the Skanyxx API (NetworkPolicy). Skanyxx enforces owner, cap, creator-or-supervisor stop. | AX has no auth (#376). |
| D073 (proposed) | Memory attach is a **contract with the task image**: read workspace MCP entry + `SKANYXX_*` env, send headers. Default runner is not supported for memory. | AX has no MCP headers and does not materialize MCP config. |
| D074 (proposed) | D068 holds **only after** a Dragonfly smoke test of AX (streams, MULTI, Pub/Sub). Until then, AX may use its own bundled Redis. | Compatibility inferred from code, not tested. |
| D075 (proposed) | Pin AX to one version (**v0.3.1** proto). Upgrades are a deliberate bump, not a chart float. | API was replaced 6 days before we read it. |
| D076 (proposed) | Memory identity for sandboxes comes from a **Skanyxx-signed per-task token** (HMAC in reserved env), not headers. On the sandbox-facing listener (D077) **every** request is identified only by that token; headers are ignored and a request without a valid token is rejected, whatever agent id it names. | A sandbox picks its own `X-Agent-Id` / `X-User-Id` today; a rule scoped to `ax-*` ids is bypassed by sending a non-`ax-` id. Threat model includes workspace writers (supervisors), whose git/MCP content runs under other owners' identities. |
| D077 (proposed) | Sandbox egress is **default-deny** (internet = `0.0.0.0/0` minus cluster CIDRs, RFC1918, `169.254.0.0/16`). A **dedicated sandbox-facing listener** (own port/Service) serves `/mcp/memory` and nothing else (`RequireHost` on the MCP map + every other endpoint kept off that port); it is the only Skanyxx port sandboxes may reach. **Not implemented**: today `/mcp/memory` is on the main pipeline, so until then `MemoryMcpUrl` stays empty and sandboxes reach no Skanyxx port. | NetworkPolicy filters ports, not paths; a second port on today's pipeline would serve the whole API. |

## 6. Open questions for the doc owner

- Accept D069 (AX off in appliance) or keep D067 and own a Substrate chart?
- What counts as "done" for a sandbox? Agent upserts a card / calls back, or "done" is not a v1 concept?
- Accept D076 + D077 (token-only sandbox listener) as the precondition for turning memory attach on?
- Does a Kubernetes NetworkPolicy apply to Substrate sandboxes (gVisor/microVM) at all? **Unverified**; the egress rules depend on it.
- Which task image do we ship that honors the memory contract (D073)? Ours, or customer-provided only?
- Atespace per install, per team, or per user?
- Do Tickets ever launch sandboxes in v1, or strictly later?
- Dedicated, quota-capped Gemini key / atespace without the platform key: who owns it?
- Who may widen `AllowedImages` (config owner only, or supervisors)?
