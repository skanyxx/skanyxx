# Skanyxx

SRE Platform for Kubernetes management, agent orchestration, and monitoring. Built on a modular plugin architecture where each UI tab (Agents, Alerts, Chat, etc.) is an independently loadable DLL.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- KAgent running locally (default: `http://localhost:8083`) or remote

## Quick Start

```bash
# Build everything (Core + Host + all 15 modules)
dotnet build Skanyxx.sln

# Run the app
dotnet run --project src/Skanyxx.Host
```

The app starts at **http://localhost:5282** (or https://localhost:7219).

Swagger UI is available at http://localhost:5282/swagger in Development mode.

## Project Structure

```
Skanyxx.sln
├── src/
│   ├── Skanyxx.Core/              # Shared contracts, models, interfaces, services
│   ├── Skanyxx.Host/              # Web host, ModuleLoader, DB, static files, Razor pages
│   │   └── modules/               # Module DLLs loaded at runtime (build output, not tracked)
│   └── Modules/
│       ├── Skanyxx.Module.Agents/
│       ├── Skanyxx.Module.Alerts/
│       ├── Skanyxx.Module.Analytics/
│       ├── Skanyxx.Module.Chat/
│       ├── Skanyxx.Module.CloudTools/
│       ├── Skanyxx.Module.Dashboard/
│       ├── Skanyxx.Module.Debug/
│       ├── Skanyxx.Module.Hooks/
│       ├── Skanyxx.Module.Investigate/
│       ├── Skanyxx.Module.Memory/     # Memory engine (Postgres cards + MCP)
│       ├── Skanyxx.Module.Sandboxes/  # AX Tasks (experimental, off by default)
│       ├── Skanyxx.Module.Sessions/
│       ├── Skanyxx.Module.Settings/
│       ├── Skanyxx.Module.Tickets/    # Ticket pipelines over kagent
│       └── Skanyxx.Module.ToolServers/
├── tests/                         # Memory, Tickets, Sandboxes test projects
├── deploy/                        # kagent ticket agents, sample tickets, sandbox NetworkPolicies
├── docs/design/                   # Design log (decisions, open questions)
└── SkanyxxWeb.csproj              # Legacy monolith (deprecated)
```

**Dependency rule:** Modules reference only `Skanyxx.Core`. Modules never reference Host or each other. Cross-module communication uses MediatR.

## Modular Architecture

Each module is a self-contained DLL that gets discovered and loaded at startup by the `ModuleLoader`. Every module implements the `IModule` interface:

```csharp
public interface IModule
{
    string ModuleId { get; }
    string DisplayName { get; }
    string Version { get; }
    IReadOnlyList<string> Dependencies { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    Task InitializeAsync(IServiceProvider serviceProvider);
}
```

On build, each module DLL is automatically copied to `src/Skanyxx.Host/modules/`. That folder is **build output**: it is not tracked in git, so `dotnet build Skanyxx.sln` must run before `dotnet run` (the binary that runs is always built from reviewed source). The Host scans that directory, loads assemblies via `PluginLoadContext`, registers controllers with `AddApplicationPart()`, and wires up DI and MediatR.

## Enable / Disable Modules

Edit `src/Skanyxx.Host/appsettings.json`:

```json
{
  "Modules": {
    "Enabled": {
      "agents": true,
      "alerts": true,
      "chat": false
    }
  }
}
```

Set any module to `false` and its API routes will not be registered. You can also simply remove the DLL from the `modules/` directory.
`sandboxes` is the exception: it is **off unless enabled** here (see AX Tasks).

## Adding a New Module

1. Create a new project under `src/Modules/`:
   ```bash
   dotnet new classlib -n Skanyxx.Module.MyFeature -o src/Modules/Skanyxx.Module.MyFeature
   ```

2. Reference Core and add the `CopyToModules` target in the `.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
     </PropertyGroup>
     <ItemGroup>
       <FrameworkReference Include="Microsoft.AspNetCore.App" />
       <ProjectReference Include="..\..\Skanyxx.Core\Skanyxx.Core.csproj" />
     </ItemGroup>
     <Target Name="CopyToModules" AfterTargets="Build">
       <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(SolutionDir)src/Skanyxx.Host/modules/" />
       <Copy SourceFiles="$(TargetDir)$(TargetName).deps.json" DestinationFolder="$(SolutionDir)src/Skanyxx.Host/modules/" />
     </Target>
   </Project>
   ```

3. Implement `IModule`:
   ```csharp
   public class MyFeatureModule : IModule
   {
       public string ModuleId => "myfeature";
       public string DisplayName => "My Feature";
       public string Version => "1.0.0";
       public IReadOnlyList<string> Dependencies => Array.Empty<string>();

       public void RegisterServices(IServiceCollection services, IConfiguration configuration)
       {
           // Register your services here
       }

       public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
   }
   ```

4. Add endpoints. New modules (Memory, Tickets, Sandboxes) use the REPR style, one class per file:
   - a FastEndpoints `Endpoint<TRequest>` per route, under a shared `Group` (route prefix, `AllowAnonymous` until the identity slice, `DisableCors`, body limit);
   - the endpoint sends a MediatR command/query; its handler returns an `Outcome<T>` that maps to HTTP;
   - a FluentValidation `AbstractValidator<TCommand>` per command (runs in the MediatR `ValidationBehavior`);
   - implement `IEndpointModule.MapEndpoints` only for routes FastEndpoints cannot see (e.g. Memory's MCP at `/mcp/memory`).

   Handlers, validators and endpoints are discovered from the module assembly (`AddSkanyxxPlatform`). Older modules still use `[ApiController]` controllers.

5. Add the project to `Skanyxx.sln`:
   ```bash
   dotnet sln add src/Modules/Skanyxx.Module.MyFeature
   ```

6. Build and run. The module is automatically discovered.

## Configuration

All configuration is in `src/Skanyxx.Host/appsettings.json`:

| Section | Description |
|---|---|
| `AllowedHosts` | Explicit host list (`skanyxx.example.com;localhost`). The Host refuses to start with `*` outside Development (DNS-rebinding guard) |
| `Skanyxx:AllowedOrigins` | Browser origins allowed on `/api/memory`, `/api/tickets`, `/api/sandboxes` and `/mcp` (default `[]`). A request carrying any other `Origin` gets `403`; requests without `Origin` (non-browser clients) pass |
| `Skanyxx:RateLimit` | Host-wide, per remote IP, on the same routes: `PermitLimit` (200) per `WindowSeconds` (10) → `429`. Behind a proxy the partition is the proxy's IP (ForwardedHeaders not configured) |
| `Skanyxx:HealthCheckTimeoutSeconds` | Per-check timeout for `/health` (5). `/health` returns status only, no CORS |
| `KAgent` | KAgent API connection (BaseUrl, Port, Protocol, Token) |
| `Kubernetes` | Optional kubeconfig path |
| `AWS` | AWS profile and region for cloud tools |
| `Azure` | Azure config directory |
| `Modules` | Plugin directory and enable/disable flags |
| `ConnectionStrings:Memory` | Postgres for the memory engine (required; startup fails without it) |
| `Memory` | `SearchTopK` (5), `UpsertsPerMinute` (30, per caller), `UpsertsPerMinuteTotal` (300, all callers), `MaxPoolSize` (40), `Supervisors` (user ids allowed to write/lift into `company` and set agent grants) |
| `ConnectionStrings:Tickets` | Postgres for ticket pipelines (required; may be the same database as memory) |
| `Tickets` | `Source` (`jira` \| `local`), `LocalPath` (JSON file, required when `local`), `Jira` (`Site` — https only, `Email`, `ApiToken`, `Project` or `Jql` (overrides `Project`), `TimeoutSeconds` (20)), `KAgentUserId` (`skanyxx-tickets`; the kagent user that owns every stage session), `StageTimeoutSeconds` (600), `PollSeconds` (5), `MaxTicketChars` (20000), `MaxPromptChars` (96000), `MaxAnswerChars` (100000), `MaxStageAttemptsPerRun` (30), `MaxActiveRunsPerUser` (5), `MaxActiveRuns` (20, all users), `MaxPoolSize` (40), `AllowedAgents` (`namespace/name`; empty = the five ticket agents; a list replaces that default), `Supervisors` (`[]`; may edit and delete pipelines) |
| `Sandboxes` | `Address` (AX gRPC, h2c; `http://ax-server.ax-system.svc:8080`), `Atespace` (`default`), `AllowedImages` (`[]`; registry/repo prefixes, empty = nothing runs, a configured list replaces the default), `RequireDigest` (`false`; on = image must be `@sha256:`), `MaxCpu` (`4`), `MaxMemory` (`8Gi`), `DefaultCpuRequest` (`250m`), `DefaultMemoryRequest` (`256Mi`), `DefaultCpuLimit` (`1`), `DefaultMemoryLimit` (`1Gi`), `MaxActiveTasksPerUser` (`3`), `MaxActiveTasks` (`50`, whole atespace), `CountBudgetSeconds` (`30`), `Supervisors` (`[]`), `NetworkIsolationConfirmed` (`false`; must be `true` for a non-empty `AllowedImages`), `MemoryMcpUrl` (`""`; a non-empty value is **refused at startup** until a sandbox-facing listener exists, see AX Tasks), `TimeoutSeconds` (`15`), `WatchSeconds` (`120`), `KeepAliveSeconds` (`15`), `MaxWatchesPerUser` (`2`), `MaxWatches` (`50`) |

## API Endpoints

Each module exposes REST endpoints under `/api/`:

| Module | Routes |
|---|---|
| Agents | `/api/agents` |
| Alerts | `/api/alerts` |
| Analytics | `/api/analytics` |
| Chat | `/api/chat` |
| CloudTools | `/api/cloud`, `/api/cloudtools` |
| Dashboard | `/api/dashboard` |
| Debug | `/api/debug` |
| Hooks | `/api/hooks` |
| Investigate | `/api/investigate` |
| Memory | `/api/memory/cards`, `/api/memory/grants` (+ MCP at `/mcp/memory`) |
| Tickets | `/api/tickets/issues`, `/api/tickets/pipelines`, `/api/tickets/runs` |
| Sandboxes (experimental) | `/api/sandboxes/tasks`, `/api/sandboxes/workspaces`, `/api/sandboxes/models` |
| Sessions | `/api/sessions` |
| Settings | `/api/settings` |
| ToolServers | `/api/toolservers` |

Health check: `GET /health` (overall status only; no per-check details)

## Security model (until the identity slice)

Memory, Tickets and Sandboxes have **no authentication yet**. Accepted for now, not safe for an open network:

- Every `*:Supervisors` entry must be a lowercase user id (startup refuses anything else).
- **One spoofable header.** All three modules identify the caller by `X-User-Id` (memory MCP also `X-Agent-Id`).
  Anyone who can reach the host can send any id, including a supervisor's.
- **Three `Supervisors` lists** — `Memory:Supervisors`, `Tickets:Supervisors`, `Sandboxes:Supervisors` — are separate;
  keep them in sync by hand.
- **Ids are visible, and an id is effectively a credential.** Run `CreatedBy` / gate `DecidedBy`, sandbox owners and
  sandbox agent ids are returned to any caller; a leaked id works in every module.
- **Jira via the service account.** Any caller can read every Jira ticket the configured Jira account can see, and
  every stored prompt (run reports and datasets hold full ticket text).
- **Jira text in kagent.** Stage sessions sit in kagent under the single user `Tickets:KAgentUserId`; whoever can act
  as that user in kagent can read them.
- **Host guards.** `AllowedHosts` must be an explicit list (the Host refuses `*` outside Development); browser clients
  must be listed in `Skanyxx:AllowedOrigins`; requests are rate limited host-wide; `/health` shows status only.
- **Deploy only on a trusted network, behind an authenticating proxy** that sets or strips `X-User-Id`.

## Memory engine

Company knowledge as small **cards** in Postgres (design: `docs/design/memory-*.md`, D034–D053).

```bash
docker compose up -d                 # Postgres on 127.0.0.1:55432 (schema is migrated at startup)
dotnet run --project src/Skanyxx.Host
dotnet test tests/Skanyxx.Module.Memory.Tests   # needs Docker (Testcontainers)
```

- A card is `scope` + `key` (`personal:ana/refund-window`), `type` (decision | fact | procedure | open), `what` ≤ 200, `why` ≤ 400; `body`/`source` are never sent to agents.
- Write with `PUT /api/memory/cards/{scope}/{key}`: `version: 0` creates, otherwise pass the version you read. A stale version is a `409` with the current card — never last-write-wins.
- `POST …/{scope}/{key}/lift` copies a card up (personal → team → department → company); the original stays. Into `company` needs a supervisor.
- kagent reaches the bank over MCP at `/mcp/memory` (`memory_search`, `memory_upsert`). Configure the agent's `RemoteMCPServer` with an `X-Agent-Id` header. Agents without grants search `company` + the calling user's personal scope and may upsert only that personal scope (needs `X-User-Id`); `PUT /api/memory/grants/{agentId}` replaces the default with explicit search/upsert per scope (at least one entry; revoke an agent with a single grant that has `canSearch` and `canUpsert` false).
- **Interim identity:** callers are identified by `X-User-Id` / `X-Agent-Id` headers, which are not authenticated yet.

The host refuses to start if the memory database is unreachable. If a local database has an older migration history, reset it with `docker compose down -v`.

## Ticket pipelines

Umbrella.Flow's ticket pipeline, run the kagent way: **Skanyxx orchestrates, kagent runs one turn per stage.**
A ticket comes from Jira (read-only) and goes through a stored pipeline — by default
`plan → review-plan → code → qa-code → review`. Each stage is one A2A `message/send` to a kagent Agent
(`deploy/kagent/ticket-flow/`, kagent 0.10.x); Skanyxx parses the verdict, loops, waits for people and keeps
every attempt in Postgres.

```bash
kubectl apply -k deploy/kagent/ticket-flow/        # the five stage agents
dotnet test tests/Skanyxx.Module.Tickets.Tests     # needs Docker; kagent is faked
```

- `GET /api/tickets/issues?assignee=` (`unassigned` for none) · `GET /api/tickets/issues/{key}` · `GET /api/tickets/issues/assignees`.
- `GET /api/tickets/runs?ticketKey=` lists runs (optionally for one ticket).
- `POST /api/tickets/runs {ticketKey, pipelineId}` → `202`; the run snapshots the ticket and the pipeline, so editing
  either later never changes it. `GET /api/tickets/runs/{id}` shows every attempt with `state` (what the run did)
  and `verdict` (what the agent said) as separate fields.
- The verdict is the **first line** of the answer, compared exactly: QA `VERDICT: PASS` / `VERDICT: FAIL`, reviews
  `RECOMMENDATION: SHIP` / `RECOMMENDATION: SHIP WITH CHANGES` / `RECOMMENDATION: DO NOT SHIP`. Nothing else in
  the answer is read, so a verdict quoted from a ticket further down never counts; any other first line (a heading,
  bold, a code fence, extra words) is `unknown`. The prompt states this contract outside every fence. Residual risk:
  a ticket that persuades the model to *open* its answer with a forged verdict is model-level prompt injection;
  the human gate at `review` is the backstop.
  A stage with `failOnVerdict` passes only on a stated pass — `FAIL` **or `unknown`** fails it (fail closed).
  `onFail: goto` sends the run back to an **earlier** stage at most `maxLoops` times (required with `goto`), and
  the re-run stage is shown the rejecting report in full. An exhausted budget fails the run and says so.
- A stage with `gate: human` waits: `POST /api/tickets/runs/{id}/decision {"decision":"approve"|"reject","note":…}`.
  A reject follows `onFail` like a failed verdict. Gates and in-flight stages survive a restart.
- In a stage with several agents, the verdict is the last agent's first line, and a `failOnVerdict` stage fails if
  any of its agents did not answer (a later PASS says nothing about work an earlier agent never did).
- An unreachable agent, or one answering in an unexpected shape, writes a deterministic fallback with a warning
  (verdict `unknown`, so a `failOnVerdict` stage fails — and stops the run rather than paying for a loop nobody
  reviewed); the run never hangs. A run whose step keeps failing on an internal error (3 tries) is marked `failed`
  with `error`.
- `GET …/runs/{id}` shows `runningStageId` while an agent works; `GET …/runs/{id}/report` is the whole run as
  markdown; `GET …/runs/{id}/dataset` is JSONL: one row per (stage, attempt, agent) with the exact prompt sent.
- `PUT` / `DELETE /api/tickets/pipelines/{id}` (supervisors). Stages may only name agents in `Tickets:AllowedAgents`
  (exact `namespace/name`, checked on save and again when a stage runs) — never kagent's own tool-bearing agents.
- Cancel (`POST …/runs/{id}/cancel`) is for the run's creator or a supervisor; an agent call already in flight
  finishes and its answer is dropped, and a multi-agent stage calls none of its remaining agents. Deciding a gate is
  likewise for the run's creator or a supervisor (`403` otherwise) — including on their own run (no four-eyes rule
  until the identity slice).
- Until the identity slice, any caller can read every run, its report and its dataset (which holds full prompts,
  including ticket text).
- Ticket text and agent output are fenced in `<data-…>` tags with a random nonce (a fresh one per handoff), and any
  `<data-` tag inside them is rewritten, so neither a ticket nor an agent can forge the sections Skanyxx writes.
- Limits: 5 unfinished runs per user (including runs waiting at a gate), 30 stage attempts per run, 100k characters
  per answer — each cut is stated. There is no global cap across users yet. When a prompt hits its ceiling, its end
  (the handoff or the rejection block) is what gets cut — stated as a warning.
- `GET …/runs/{id}/dataset` and `…/report` are built in memory (bounded by the limits above).
- Nothing is written outward: Jira is only read (GET), and the Code stage proposes the change as text.
- The run worker is in-process and steps runs round-robin; only the replica holding a Postgres advisory lock
  works, and it re-checks the lock before every step, so several replicas (or a rolling update) pay twice for at
  most the one in-flight step on failover. The lock is a session lock: put no transaction-pooling proxy (PgBouncer
  transaction mode) in front of Postgres. Replicas starting together migrate and seed one at a time. One slow
  stage delays the others.
- All stage sessions in kagent belong to one user (`Tickets:KAgentUserId`); anyone who can act as that user in
  kagent can read them. Point `KAgent` at an in-cluster or https URL when a `Token` is set.
- Not ported from Umbrella.Flow: the `research` and `summarise` stage kinds, its two other default pipelines, the
  model-backed `acceptance_criteria` skill, the SSE progress stream, the `/stage-kinds` and `/skills` listings,
  and per-agent provider/model overrides (the model is the kagent
  agent's `ModelConfig`, so dataset rows name the agent, not the model).

## AX Tasks (Experimental)

Runs container tasks in [Google AX](https://github.com/google/ax) sandboxes (Agent Substrate). Pinned to AX
**v0.3.1** — the client is generated from its vendored `ax.proto` (`src/Modules/Skanyxx.Module.Sandboxes/Protos/`);
AX is pre-1.0 and rewrote its API in v0.3.0, so re-vendor deliberately. **Not verified on a real Substrate/AX
cluster** — only against an in-process fake AX gRPC server.

```bash
dotnet test tests/Skanyxx.Module.Sandboxes.Tests   # no Docker needed; AX is faked
```

**Off unless enabled.** Turn it on with `Sandboxes:Enabled: true`; while off the module registers nothing and every
`/api/sandboxes` route answers `404`. Startup refuses a non-empty `AllowedImages` unless `Sandboxes:NetworkIsolationConfirmed=true` (set it only
after the NetworkPolicies below are applied and verified), and refuses any non-empty `MemoryMcpUrl`.

- `GET /api/sandboxes/tasks?limit=&offset=` · `GET …/tasks/{name}` · `PUT …/tasks/{name}` (run = AX `UpdateTask`,
  create-or-update; body `{image, command[], env{}, workspaces[{name, path?, goal?}], resources?{requests, limits}}`;
  new → `202`, update → `200`) · `POST …/tasks/{name}/stop` (AX `DeleteTask`; async, `202`) ·
  `POST …/tasks/{name}/suspend` (only a Pending/Running task) · `POST …/tasks/{name}/resume` (only a Suspended
  task); anything else is `409` — a Failed/Terminating task is re-run with `PUT`, which is counted against the caps.
- `GET …/tasks/{name}/watch` is `text/event-stream`: AX's `initial`/`modified` frames, then one `final` (a fresh
  `GET`), `gone` or `error` frame. AX closes its watch once the task is Running/Failed, so poll `GET` afterwards.
- `GET /api/sandboxes/workspaces` · `PUT …/workspaces/{name}` `{git[{repo, branch?, dir?, depth?}], mcpServers[{name,
  endpoint}], attachMemory}` (supervisors only: a workspace is shared by every task that binds it) ·
  `GET /api/sandboxes/models`.
- Names are AX's: DNS labels (lowercase, `-`, ≤ 63). There is **no local run table** — AX is the source of truth.
- Owner: AX v0.3.1 has no labels, so the creator is stored in the task's env as `SKANYXX_OWNER`. Every `SKANYXX_*`
  env name is reserved (`400` if a caller sets one). Only the creator may run/replace a task; stop/suspend/resume
  is for the creator or a `Sandboxes:Supervisors` user (`403` otherwise). Supervisors cannot replace another
  user's task. Tasks created outside Skanyxx are supervisors' only.
- What may run: `image` must match an `AllowedImages` prefix (empty list = every run is `400`). A prefix matches
  only at a boundary: `/`, `@`, or a `:` that starts a tag (no `/` after it), so `ghcr.io` does not admit the
  registry port `ghcr.io:5000/…`. End prefixes in `/` (`ghcr.io/acme/`). `RequireDigest` also demands `@sha256:`. `requests`/`limits` above `MaxCpu`/`MaxMemory` (default `4` / `8Gi`) are `400`; missing
  ones get the defaults (requests `250m`/`256Mi`, limits `1`/`1Gi`).
- Caps (`429`): global `MaxActiveTasks` (50) and per-user `MaxActiveTasksPerUser` (3), counting tasks that are not
  Failed/Completed/Terminating (suspended count), via `ListTasks`. Re-running a stopped/failed task counts as a
  new one. Every run that makes a task active counts and writes under one atespace-wide lock, so parallel runs
  under different `X-User-Id`s cannot pass the global cap together; replacing an already-active task is not
  counted and stays parallel. Activating runs are serialised across the atespace, so the caps are exact within one replica (16 in flight on
  the lock; the next gets `429`); best-effort across replicas (AX has no conditional write).
  Counting pages the **whole atespace** on every activating run (AX v0.3.1 `ListTasks` has no owner/label filter),
  so it is O(tasks) and activations are serialised behind it; paging stops only on an empty page; AX orders its index by last save, so a task saved between pages can still
  be missed (approximate). If the count
  cannot finish within `CountBudgetSeconds` (30), or the atespace holds more than 10,000 tasks, the run is refused (`502`,
  logged) rather than admitted on an undercount.
- Watch: at most 2 open per user and 50 in total (`429`, checked before AX is asked — so an over-cap watch of a
  missing task is `429`, not `404`), each ends after `WatchSeconds` (120); a `: keepalive`
  comment is sent every `KeepAliveSeconds` (15) while AX is quiet, and `X-Accel-Buffering: no` is set.
- Env **values** are never returned (only names), but env is **not a secret store**: AX keeps it in plaintext
  (Redis, ActorTemplate) and serves it from the task metadata endpoint. Never put secrets in env or `command`
  (`command` is returned to every caller). `debug` (unauthenticated guest services) is never set.
- Errors: AX `NotFound` → `404`, `InvalidArgument` → `400`, `AlreadyExists`/`FailedPrecondition`/`Aborted` → `409`,
  `Unavailable`/`DeadlineExceeded` → `502 "AX unreachable."`, anything else → `502 "AX request failed."`. AX's own
  error text and condition messages are logged, never returned (conditions carry type/status/reason/time only).
- **Memory wiring.** `attachMemory: true` adds the MCP entry `{name: "skanyxx-memory", endpoint:
  Sandboxes:MemoryMcpUrl}` (off: a non-empty `MemoryMcpUrl` is refused at startup, see below). Each time a task becomes
  active it gets a fresh agent id `ax-<name>-<nonce>` (`SKANYXX_AGENT_ID`, also the run response's `agentId`; the
  owner replacing a still-active task keeps it) and `SKANYXX_USER_ID=<owner>`, so a reused task name never
  inherits old grants. AX has
  no MCP headers field and its default runner does not materialise MCP config, so **the task image must read
  `AX_WORKSPACES_YAML` (or its metadata URL) and send `X-Agent-Id: $SKANYXX_AGENT_ID` and
  `X-User-Id: $SKANYXX_USER_ID`** to `/mcp/memory`. Without grants that agent searches `company` + the user's
  personal scope; a supervisor can widen it with `PUT /api/memory/grants/<agentId>`. Modules do not call each
  other, so Sandboxes never writes grants itself.

**Deployment prerequisites (security).** AX has no authentication (google/ax#376); these are not optional.
NetworkPolicy manifests for both rules below are in `deploy/sandboxes/` (`ax-server-ingress.yaml`,
`sandbox-egress-deny.yaml`, optional `sandbox-egress-internet.yaml`): `kubectl apply -k deploy/sandboxes/`
(unverified on Substrate sandboxes — see its README).

- NetworkPolicy on `ax-system`: only Skanyxx's pods reach `ax-server:8080` (keep `ax-controller` ↔ Redis open).
- Default-deny **egress** for sandbox pods, then explicit allows: DNS, and internet only as `0.0.0.0/0` **except**
  the cluster pod/service CIDRs, RFC1918 (`10/8`, `172.16/12`, `192.168/16`) and link-local `169.254.0.0/16`.
  Must block: kube-apiserver, cloud metadata `169.254.169.254`, AX Redis/Dragonfly, the Substrate API,
  `atenet-router` (its metadata path serves other tasks' env) and every Skanyxx port. **Unverified:** whether a
  Kubernetes NetworkPolicy applies to Substrate sandboxes at all (they run under gVisor/microVM) — confirm on the
  target cluster before relying on it.
- **Sandboxes must not reach Skanyxx at all; `MemoryMcpUrl` stays empty (enforced at startup).** A separate memory port is **not
  implemented**: `/mcp/memory` is mapped on the main pipeline, so any extra Kestrel port/Service serves the whole
  API (`/api/sandboxes/*`, `/api/memory/*`, Tickets, kagent proxies) — and with a spoofed supervisor `X-User-Id` a
  sandbox could stop anyone's tasks, rewrite workspaces and write any user's memory. A NetworkPolicy filters ports,
  not paths.
- AX injects `GEMINI_API_KEY` into **every** task. Run Skanyxx's atespace in a namespace without the platform
  key (no `gemini-api-secret`, none in the controller env), or use a dedicated quota-capped key.
- Env is plaintext in AX and visible via task metadata — never put secrets in env.
- AX's Redis must use `maxmemory-policy noeviction` and no TTL on task records: an evicted or expired record
  makes the cap count stop early and admit extra tasks. So no shared LRU Dragonfly for AX (see D068/D074).

**Known limitations.**

- One caller with made-up user ids can keep 16 task starts in flight, so other starts get `429` (stop/suspend/
  replace still work). AX never removes Failed tasks: past 10,000 records every start is `502` — stop (delete)
  finished tasks regularly.
- A replace or suspend that races an AX-side failure can reactivate a task without counting it (+1 per race,
  millisecond window; AX has no conditional write).
- If AX cannot build a task's own template it falls back to its default image, which `AllowedImages` does not see.
- The startup checks trust the operator's `NetworkIsolationConfirmed`; nothing in Skanyxx can see whether the NetworkPolicies actually apply to Substrate sandboxes.

- Memory attach is not safe yet. A sandbox chooses the `X-Agent-Id` / `X-User-Id` it sends, so any sandbox that
  can reach the memory endpoint can act as any agent and any user. The image does not bound this — `command` is
  arbitrary for every caller, so an allow-listed image with a shell runs anything — and neither does
  `attachMemory`, which only declares the entry; what a sandbox reaches is decided by the network. Memory attach
  becomes safe only with both pieces of future work: a **dedicated sandbox-facing listener** that serves
  `/mcp/memory` and nothing else, and a **Skanyxx-signed per-task token** (HMAC in reserved env) from which that
  listener derives identity for **every** request — not only `ax-*` agent ids, or a sandbox would just send a
  non-`ax-` id.
- The global watch cap (50) can be filled by one caller under made-up user ids, locking others out of watching;
  polling `GET` still works.
- Non-owners can fill a task's lock queue (16) with stop/suspend/resume requests that end in `403`, so the owner's
  own actions on that task can get `429` while AX is slow.
- Workspace writers (supervisors) influence other users' tasks: a task binding a workspace runs its git content and
  MCP endpoints under the task owner's `SKANYXX_USER_ID` memory identity.
- Caller identity is the `X-User-Id` header, spoofable until the identity slice: anyone who sends a supervisor's
  id is a supervisor. Do not expose these routes beyond a trusted network.

On a machine with only the .NET 10 runtime, prefix `dotnet run` / `dotnet test` with `DOTNET_ROLL_FORWARD=Major`.
