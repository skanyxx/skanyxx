# Skanyxx

SRE Platform for Kubernetes management, agent orchestration, and monitoring. Built on a modular plugin architecture where each UI tab (Agents, Alerts, Chat, etc.) is an independently loadable DLL.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` pins 10.0.100 or a later 10.0 feature band, no previews; every project targets `net10.0`)
- KAgent running locally (default: `http://localhost:8083`) or remote

## Quick Start

```bash
# Build everything (Core + Host + all 16 modules)
dotnet build Skanyxx.sln

# Run the app
dotnet run --project src/Skanyxx.Host
```

The app starts at **http://localhost:5282** (or https://localhost:7219). It needs Postgres (`docker compose up -d`).
On first run, `/Setup` creates the owner account. With `dotnet run` (Development) and no token configured, that
works from the same machine only (`http://localhost:5282`). Anywhere else — Production, the installers, a container —
set `Identity:BootstrapToken` (32+ characters) first and enter it in the setup form; the Linux and macOS installers
generate one for you. See Identity and security model.

Swagger UI is available at http://localhost:5282/swagger in Development mode.

## First hour (local three-plane run)

The first hour (`docs/design/first-hour.md`): `/Setup` creates the owner → the owner sets the model (`/Model`) → Chat
with the **seed** agent, whose answers search company memory. Three planes run locally: **Postgres**
(`docker compose`), **kagent 0.10.2** on any Kubernetes, and the **Host** (`dotnet run`).
`scripts/dev/first-hour.sh` does each step and is safe to re-run.

Needs: Docker, `kubectl`, `helm`, `jq`, `lsof`, `pgrep`, a Kubernetes context (kind is one option: a `kind-<name>` context gets its kind
cluster created when missing; any other context must exist), and a model — by default the machine's Ollama serving
`qwen3-coder:30b` (`ollama pull qwen3-coder:30b`; another provider can be chosen in `/Model`).

```bash
export KUBE_CONTEXT=kind-skanyxx            # default: the current context
scripts/dev/first-hour.sh up                # Postgres; kagent (sample agents off, D019); deploy/kagent/memory/; port-forward :8083
scripts/dev/first-hour.sh host              # the Host on http://localhost:5287 (foreground)
# browser: http://localhost:5287/Setup → owner → /Model (save) → /Chat
OWNER_EMAIL=… OWNER_PASSWORD=… scripts/dev/first-hour.sh seed-secret   # once per database; again to rotate
scripts/dev/first-hour.sh git               # bundled git for the studio (Gitea on :3300); then restart `host`
scripts/dev/first-hour.sh status
```

- **`up`** applies **only** `deploy/kagent/memory/` (seed Agent + its RemoteMCPServer), with the in-cluster Skanyxx
  URL replaced by `http://$HOST_FROM_PODS:$PORT/mcp/memory`. `HOST_FROM_PODS` (default `host.docker.internal`) is how
  pods reach this machine. kagent is installed once from `scripts/dev/kagent-values.yaml`; an existing release is left
  alone, because a `helm upgrade` would overwrite the model the owner saved (D099). Ollama settings:
  `OLLAMA_MODEL`, `OLLAMA_HOST` (as pods see it), `OLLAMA_NUM_CTX`.
- **`host`** creates database `$DB` (default `skanyxx`) if missing and runs the Host with env config only:
  Development, `$HOST_BIND:$PORT` (`127.0.0.1:5287`), `AllowedHosts` including `$HOST_FROM_PODS`,
  `Identity:PublicBaseUrl` (whose origin Chat's fetch is allowed from, D108; `http://localhost:$PORT` for a loopback bind,
  else the bind address) plus the bind address in `Skanyxx:AllowedOrigins`, `KAgent` at `localhost:8083` with a
  300 s chat turn (a local model with tool calls is slow), Tickets from the sample file. On Docker Desktop and OrbStack
  the pods' `host.docker.internal` reaches this machine's loopback, so `127.0.0.1` is enough; on plain Linux Docker
  bind the docker bridge address (`HOST_BIND=172.17.0.1`), never `0.0.0.0` (a Development Host exposes Swagger and
  `/Setup`). `up` keeps the kagent port-forward alive in a restart loop (`status` shows it) and stops with a clear
  message when the kagent Helm release exists but is not `deployed`. Every process listening on `:8083` (IPv4 and IPv6)
  must run under that loop for `$KUBE_CONTEXT`: the script's loops for another context, and duplicates, are stopped with
  everything under them (a loop is recognised by its exact command line: nothing that is not such a loop or under one
  is ever signalled), anything else listening (an old hand-made port-forward, another cluster) stops `up`
  with exit 1, and so does a forward that never answers `/health` — instead of silently sending `/Model` and Chat to
  the wrong kagent (D176). `status` names who holds `:8083`.
- **`seed-secret`** signs in as the owner, issues the seed's memory secret with `actsForUsers: true` (D084; owner only),
  writes it into the Secret the seed's RemoteMCPServer reads and bumps the Agent's label so kagent re-renders it
  (the rotation runbook, `deploy/kagent/memory/README.md`). The secret never touches a file, argv or the log, and
  the Secret is written with server-side apply, so no `last-applied-configuration` annotation holds a copy of it.
  Until it runs, the seed's memory calls answer `401`.

**Model (owner only):** `/Model` or `GET`/`PUT /api/model` `{provider, model, apiKey?, baseUrl?}` — providers `OpenAI`,
`Anthropic`, `Ollama`. It edits kagent's ModelConfig `KAgent:ModelConfig` (default `kagent/default-model-config`)
through kagent's API; a pasted key goes to kagent, which keeps it in a Secret it owns. Skanyxx never stores, shows or
logs it, and needs no Kubernetes credentials. An empty key keeps the current one (same provider and same base URL —
a new base URL needs the key pasted again, `409`, so a stored key is never sent to a new host); switching provider
starts a clean ModelConfig and needs that provider's key, and the old provider's key is removed from kagent's Secret
(saved once without a key, which makes kagent delete the Secret, then with the new one; D099, D106). Ollama needs its
host (`baseUrl`); the owner may point it at any URL the agent pods reach — trusted, but an SSRF reach from the agent
pod all the same. kagent 0.10.2's update takes no version, so concurrent saves are last-writer-wins. `/` sends the
owner here until kagent has accepted a model with its credentials; everyone else lands on Chat.

**Chat:** lists only **merged** agents — Agents labelled `skanyxx.dev/merged: "true"` (the seed today; D100) — via
`GET /api/chat/agents`; `POST /api/chat {agentNamespace, agentName, message, conversationId?}` talks to kagent as the
signed-in person (kagent's `user_id`/`X-User-Id`), so the seed searches company memory and that person's personal
memory (D101). An agent is `namespace/name`. A failing turn answers `502` with a fixed text (kagent's own text stays in
the log) and deletes the conversation it had just started. Chat is the only place that talks to an agent: the leftover
Investigate page no longer does (D002, D017).
kagent's API is unauthenticated in this setup (`auth.mode: unsecure`): keep it on localhost / inside the cluster.

## Local stack on OrbStack

The dev stack runs on [OrbStack](https://orbstack.dev) — its Docker for compose and Testcontainers, its Kubernetes for
kagent (D177). Same script, no kind:

```bash
open -a OrbStack                                  # first run: finish its setup in the window (choose Docker;
                                                  # "Migrate from Docker Desktop" is not needed for Skanyxx)
orb config set k8s.enable true                    # only if `orb config show` says k8s.enable: false; context `orbstack`
export KUBE_CONTEXT=orbstack DOCKER_CONTEXT=orbstack
scripts/dev/first-hour.sh up                      # Postgres :55432 + kagent 0.10.2 + seed + port-forward :8083
scripts/dev/first-hour.sh git                     # Gitea on 127.0.0.1:3300 and its token
scripts/dev/first-hour.sh host                    # then /Setup → /Model → seed-secret → /Chat as above
```

- Pods reach this machine's loopback through `host.docker.internal` (Ollama on `127.0.0.1:11434`, the Host's
  `/mcp/memory` on `127.0.0.1:5287`), so the defaults (`HOST_FROM_PODS`, `HOST_BIND=127.0.0.1`) work unchanged.
- OrbStack's setup makes `orbstack` the current Docker context and, when it was given admin access, points
  `/var/run/docker.sock` at its own socket, so `dotnet test` (Testcontainers) uses it without settings. Otherwise, or
  with Docker Desktop still the current context, run the suites with `DOCKER_HOST=unix://$HOME/.orbstack/run/docker.sock`.
- `DOCKER_CONTEXT=orbstack` keeps the script's `docker compose` on OrbStack whatever the current context is. Docker
  Desktop and OrbStack each publish their own containers on this machine's ports, so the compose project `skanyxx`
  (55432, 3300) must run on one of them only.
- The OrbStack VM's memory cap (`orb config show`, `memory_mib`) needs room for kagent and its agents; the model runs in
  Ollama on the Mac, outside it.
- Back to Docker Desktop + kind: `docker --context orbstack compose -p skanyxx down` (volumes kept), then
  `DOCKER_CONTEXT=desktop-linux KUBE_CONTEXT=kind-skanyxx scripts/dev/first-hour.sh up` (compose and the missing kind
  cluster both follow `DOCKER_CONTEXT`, so set it even when OrbStack is the current context). Each runtime keeps its
  own volumes: the databases do not follow.

## Install with Helm

The product is one chart, `deploy/helm/skanyxx` (D060): the Skanyxx API/UI, **kagent 0.10.2**, and the bundled data
plane — **Postgres**, **MinIO**, **Gitea** (git) and **one Dragonfly** (D061, D068). Here Skanyxx runs **in the
cluster** and is the source of truth for people, org and cards; a desktop or `dotnet run` Host is a console that
points at it (D059). One install per customer, in one namespace (some names are fixed: `kagent-*`, `dragonfly`) —
and with the bundled kagent **one install per cluster**: kagent's CRDs are cluster-wide and owned by the release
(D136). With `kagent.enabled=false` (your own kagent) several installs can share a cluster. Installing into a
namespace other than `skanyxx` needs `--set 'kagent.rbac.namespaces={<ns>}'` (the chart refuses otherwise).

```bash
scripts/helm/build-image.sh                        # Dockerfile → skanyxx:dev (KIND_CLUSTER=<name> also loads it into kind)
helm dependency build deploy/helm/skanyxx          # kagent, kagent-crds, dragonfly (pinned in Chart.lock)
helm install skanyxx deploy/helm/skanyxx -n skanyxx --create-namespace \
  --set skanyxx.publicUrl=http://localhost:8080    # or ingress.enabled + ingress.hosts (publicUrl defaults to https://<host>)
kubectl -n skanyxx port-forward svc/skanyxx 8080:8080       # UI/API only; /mcp/memory is on 8081 (in-cluster)
kubectl -n skanyxx get secret skanyxx -o jsonpath='{.data.bootstrap-token}' | base64 -d   # for /Setup
# browser: http://localhost:8080/Setup → owner → /Model (save) → /Chat
OWNER_EMAIL=… OWNER_PASSWORD=… scripts/helm/seed-agent.sh   # the seed agent + its memory secret; again to rotate
scripts/helm/validate.sh                           # helm lint + kubeconform -strict: default, BYO, AX + 18 refusals
```

- **Image** (`Dockerfile`): .NET 10, multi-stage; the Host plus all 16 module DLLs in `/app/modules`; runs as the
  non-root `app` user (1654) with a read-only root filesystem; no configuration or secrets inside — the chart sets
  everything through env (`ASPNETCORE_ENVIRONMENT=Production`, `AllowedHosts`, `Identity__*`, `KAgent__*`,
  `ConnectionStrings__*` from a Secret). The leftover SQLite file lives on an emptyDir (`/data`).
- **Secrets** are generated once and kept across `helm upgrade` (`lookup`): the bootstrap token (48 characters),
  Postgres role passwords, MinIO keys, Gitea admin + SECRET_KEY, the Dragonfly password. They are also **kept on
  `helm uninstall`** (`helm.sh/resource-policy: keep`), like the PVCs: a reinstall under the same release name reads
  them back, so the kept Postgres roles and Gitea's SECRET_KEY still match. To start over, delete those Secrets with
  the PVCs. Or name your own: `skanyxx.bootstrapToken.existingSecret`, `skanyxx.database.existingSecret`,
  `postgresql.existingSecret`, `minio.existingSecret`, `gitea.existingSecret`,
  `dragonfly.passwordFromSecret.existingSecret.name`. **GitOps (Argo CD, Flux) must set them all:** `helm template`
  sees no cluster, so `lookup` would draw new passwords on every sync (D139). Postgres applies its role passwords once
  (initdb): rotating one is `ALTER ROLE` plus the Secret, never the Secret alone. A chart-generated Secret
  never changes by itself; a changed BYO Secret (`skanyxx.*` existingSecret, token, certificate) rolls the Skanyxx
  pod on the next `helm upgrade` (checksum annotation via `lookup`), otherwise `kubectl rollout restart deploy/skanyxx`.
  The bootstrap token stays after `/Setup`: it also guards `unlock` and the owner's break-glass sign-in. Postgres gets **separate roles and
  databases**: `skanyxx_identity` (the Data Protection key ring), `skanyxx` (memory + tickets), `kagent`.
  `skanyxx.dataProtection.certificateSecret` mounts a PFX that encrypts the key ring.
- **The model stays the owner's:** the chart renders **no** `default-model-config` (`kagent.providers: false`); `/Model`
  creates it through kagent's API, so an upgrade never puts a chart spec back over the owner's (D099, D131).
- **kagent** starts clean (D019: every sample agent and tool server off, its UI at 0 replicas — Chat is Skanyxx's) and
  uses the umbrella's Postgres. The seed is not part of the release (its Agent kind only exists once the release's CRDs
  are in, and its secret needs an owner): `scripts/helm/seed-agent.sh` (env `RELEASE`, `NAMESPACE`, `BASE`) checks
  its kubectl rights first, issues the secret (`actsForUsers: true`, D084; this revokes the previous one), stores it,
  applies `deploy/kagent/memory/` into the namespace pointed at the release's own Service and mcp port
  (`http://skanyxx.<ns>.svc:8081/mcp/memory`), and bumps the Agent's label. It refuses a clear-text `BASE` that is not
  loopback (`--insecure` to override). kagent's RBAC is **Role + RoleBinding in the release namespace** and it watches
  only that namespace (`kagent.rbac.namespaces`, D136) — not cluster-wide Secret access. **`helm uninstall` deletes
  kagent's CRDs and with them every Agent and ModelConfig in the cluster** (they are chart templates of kagent-crds);
  `kagent.enabled=false` on an upgrade would do the same, so the chart refuses it unless `kagent.confirmCrdRemoval=true`
  (D141). `kagent.providers` must stay `false` (the chart refuses a value: Helm would own the owner's model, D142).
- **`/mcp/memory` has its own port** (8081, `skanyxx.service.mcpPort` → `Memory:McpPort`): the Host serves it there
  only (by the connection's local port, not the Host header) and not at all on the UI/API port 8080, which is the only
  one the Ingress targets (D138); and that port serves nothing else — the UI, the API, `/health` and static files
  answer 404 there. The Host logs a warning at startup when `Memory:McpPort` is none of the ports it listens on. The **Ingress is also an allow-list** of path prefixes (`ingress.prefixPaths`, the
  product pages only — not the leftover SRE pages): the chart refuses `/`, any path under `/mcp` in any case, anything
  but plain path segments, and the nginx `use-regex` / `rewrite-target` / `*-snippet` annotations. Prefix matching is
  case-sensitive, so a new page must be listed with the casing it links to. TLS ends at the ingress: set **HSTS there**;
  the app does not trust `X-Forwarded-*` headers (D143). kagent's controller has no Ingress.
- **One-time links in proxy access logs:** invite and password-reset links carry their token in the query string
  (`/Invite?token=skx_inv_…`, `/ResetPassword?token=skx_rst_…`). Skanyxx never logs it, but a reverse proxy's or
  ingress controller's **access log records query strings by default** (ingress-nginx's `$request`, Traefik, an ALB's
  access logs). Anyone who can read those logs holds a live invite or reset link until it is used or expires (7 days /
  60 min). Either drop the query from the access-log format for `/Invite` and `/ResetPassword` (ingress-nginx:
  `log-format-upstream` with `$uri` instead of `$request`), or treat the access logs as secret as the database.
- **Network:** the namespace is **default-deny** (`networkPolicy.enabled`, D137): a pod no policy names gets nothing in
  and only DNS out. Skanyxx's UI/API port answers its namespace and, with the ingress on,
  `networkPolicy.ingressNamespaces` (`ingress-nginx`); its mcp port only kagent's controller and agent pods (plus
  `networkPolicy.mcpFrom`); kagent's controller its namespace; agent pods only the controller (A2A); Postgres, MinIO, Gitea and Dragonfly only Skanyxx
  (+ kagent → Postgres, + `ax.namespace` → Dragonfly when AX is on). Egress: Skanyxx and kagent's controller anywhere
  (Jira, Entra, the Kubernetes API); agent pods only kagent's controller, Skanyxx's mcp port (never its UI/API) and
  `networkPolicy.modelEgress` (the internet on 443 minus `networkPolicy.clusterCidrs`; a local model such as Ollama or
  a tool server you add needs a rule in `modelEgress.extra`) (D148); the stores DNS only. `clusterCidrs` defaults to
  the private ranges (RFC 1918, CGNAT, link-local incl. cloud metadata, loopback, IPv6 ULA/link-local): on **IPv6 or
  dual-stack clusters with global (GUA) pod/Service ranges** (EKS IPv6, GKE dual-stack) or clusters on **public IPv4
  pod/Service CIDRs**, add those ranges to `networkPolicy.clusterCidrs`, or agents can reach in-cluster services on 443
  (D149).
  In-cluster traffic is plain http (`sslmode=disable`, `/mcp/memory`): encryption (a mesh with mTLS) is the operator's
  (D133).
- **Upgrades:** `postgresql|minio|gitea.storage` and `.storageClassName` are StatefulSet volume templates, which no
  upgrade may change — resize by editing the PVC (if the StorageClass allows expansion), then
  `kubectl delete sts <name> --cascade=orphan` and upgrade. `values.schema.json` rejects unknown keys and wrong types
  (a typo such as `postgres.enabled` fails instead of being ignored). Images: MinIO, Postgres and Gitea are pinned by
  digest (`*.image.digest`); `skanyxx.image.digest` pins yours. `skanyxx:dev` with `IfNotPresent` suits kind; a node
  never re-pulls a mutable tag, so ship a new tag or a digest (D140).
- **Bring your own (D061):** `postgresql.enabled=false` + `skanyxx.database.existingSecret` (keys
  `ConnectionStrings__Identity`, `__Memory`, `__Tickets`); `kagent.enabled=false` + `skanyxx.kagent.url`;
  `minio.enabled` / `gitea.enabled` / `dragonfly.enabled=false` + `external.objectStore|git|redis`. Skanyxx reads no S3,
  git or Redis yet: where each store is lands in the ConfigMap `<release>-stores` for the slices that will.
  With BYO Postgres (or `postgresql.existingSecret`) and the bundled kagent, create the Secret `kagent-postgres-url`
  (key `url`) yourself; NOTES warns when it is missing. **With BYO kagent, admit it to the mcp port:** the namespace is
  default-deny and only the bundled kagent is admitted by itself, so set `networkPolicy.mcpFrom` to your kagent's
  peers, e.g. `[{namespaceSelector: {matchLabels: {kubernetes.io/metadata.name: kagent}}}]` (its controller discovers
  the tools, its agent pods call them; D147), and point its RemoteMCPServer at
  `http://<release>.<ns>.svc:8081/mcp/memory` — port 8081, not 8080. NOTES warns when nothing is admitted.
  Example: `deploy/helm/skanyxx/ci/byo-values.yaml`.
- **AX** (`ax.enabled`, default **false**): D069 (locked 2026-10-08) keeps it off in the appliance and on-by-flag for BYO clusters that meet Substrate's prerequisites.
  The chart does not install AX; `ax.enabled` turns the sandboxes module on and points it at your AX (`ax.address`).
- **MinIO image:** MinIO's own images are gone (Docker Hub repo removed, quay `401`, GitHub archived); the default is
  the community fork `pgsty/minio` (D132). `minio.image.*` takes another.
- **Not built:** the appliance (hidden k3s + this chart, D057) — D069 is locked (2026-10-08), so it is no longer blocked on that. Shown on kind (evidence:
  task `2026-10-06_0900_skanyxx-helm`, evidence/e2e.md and e2e-qa1.md); no real ingress or cloud cluster yet.

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
│       ├── Skanyxx.Module.Identity/   # Accounts, owner bootstrap, sign-in (ASP.NET Core Identity)
│       ├── Skanyxx.Module.Investigate/
│       ├── Skanyxx.Module.Memory/     # Memory engine (Postgres cards + MCP)
│       ├── Skanyxx.Module.Sandboxes/  # AX Tasks (experimental, off by default)
│       ├── Skanyxx.Module.Sessions/
│       ├── Skanyxx.Module.Settings/
│       ├── Skanyxx.Module.Tickets/    # Ticket pipelines over kagent
│       └── Skanyxx.Module.ToolServers/
├── tests/                         # Host, Identity, Memory, Tickets, Sandboxes test projects
├── deploy/                        # Helm umbrella (helm/skanyxx), kagent seed + ticket agents, sample tickets, sandbox NetworkPolicies
├── docs/design/                   # Design log; build order is docs/design/todo.md
└── AGENTS.md                      # Remarks for AI agents working in this repo
```

**Dependency rule:** Modules reference only `Skanyxx.Core`. Modules never reference Host or each other. Cross-module communication uses MediatR.

The old root monolith (`SkanyxxWeb.csproj` and its `Controllers/`, `Pages/`, `wwwroot/`, … at the repo root) is gone
(D171): `Skanyxx.sln` is the whole product. Build output (`bin/`, `obj/`) is never tracked.

**Tests and releases.** Run each suite on its own (`dotnet test tests/Skanyxx.Host.Tests`, and the four module
suites); Host, Identity, Memory and Tickets start Postgres through Testcontainers, so they need Docker. The release
workflow (`.github/workflows/release.yml`) builds the solution and runs all five suites on Ubuntu first: no installer
is built unless they pass (D173).

**Outbound HTTP.** No HTTP client in the solution has a retry handler: kagent (A2A `message/send`, ModelConfig
writes), Gitea merges, Jira, Graph and AX are sent once at the transport level, under their own timeouts. The
unused Polly retry client was removed (D170); nothing in the solution references Polly.

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
      "hooks": false
    }
  }
}
```

Set any module to `false` and its API routes will not be registered. You can also simply remove the DLL from the `modules/` directory.
`sandboxes` is the exception: it is **off unless enabled** here (see AX Tasks).

**Disabling a module that another enabled module depends on stops startup** (`Module '<id>' needs module '<dep>'…`), and so
does a dependency whose DLL is missing or fails to load: disable the dependents too. Current dependencies (each module's
`Dependencies`; every listed one is a module whose MediatR requests or services it uses):

| Module | Needs |
|---|---|
| `dashboard` | `agents`, `alerts`, `analytics`, `cloudtools` |
| `debug` | `agents`, `alerts` |
| `investigate` | `agents`, `chat` |
| `memory` | `identity` (team membership through `IOrgMembership`) |

Nothing depends on `dashboard`, `debug`, `investigate`, `memory`, `hooks`, `sessions`, `settings`, `tickets`,
`toolservers` or `sandboxes`, so each of those can be disabled on its own.

## Adding a New Module

1. Create a new project under `src/Modules/`:
   ```bash
   dotnet new classlib -n Skanyxx.Module.MyFeature -o src/Modules/Skanyxx.Module.MyFeature
   ```

2. Reference Core and add the `CopyToModules` target in the `.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
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
   - a FastEndpoints `Endpoint<TRequest>` per route, under a shared `Group` (route prefix, `DisableCors`, body limit); every route requires a signed-in user unless it calls `AllowAnonymous()`, and reads the caller with `Caller.UserId(User)`;
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
| `Skanyxx:AllowedOrigins` | Browser origins allowed to send unsafe requests (POST/PUT/PATCH/DELETE) to **any** route on the Host, plus every request to `/api/memory`, `/api/tickets`, `/api/sandboxes`, `/api/identity`, `/api/chat`, `/api/model` and `/mcp` (default `[]`). The origin of `Identity:PublicBaseUrl` is allowed too (D108), so the Chat page's `POST /api/chat` works from the public address without listing it; any other address the page is opened under must be listed (`first-hour.sh host` lists the bind address). Outside Development, neither set logs a startup warning (Chat would get `403`). On routes outside those prefixes, the Host's own origin (Origin host:port equal to the Host header) is also allowed, so the app's own forms and fetches work without listing it. Any other `Origin` gets `403`; requests without `Origin` (non-browser clients) pass |
| `Skanyxx:RateLimit` | Per client address (IPv6 grouped per /64), on the guarded prefixes only (`/api/memory`, `/api/tickets`, `/api/sandboxes`, `/api/identity`, `/api/chat`, `/api/model`, `/mcp`); legacy controllers, pages and static files are not limited: `PermitLimit` (200) per `WindowSeconds` (10) → `429`. Behind a proxy the partition is the proxy's IP (ForwardedHeaders not configured) |
| `Skanyxx:SignInRateLimit` | Stricter window, per client address (IPv6 per /64), over the credential posts only — sign-in, bootstrap, unlock, `/Login`, `/Setup`, "forgot your password?" (`POST /api/identity/password/forgot`, `/ForgotPassword`): `PermitLimit` (10) per `WindowSeconds` (60) → `429`. Sign-out and refresh are outside it |
| `Skanyxx:InviteRateLimit` | Its own window, per client address (IPv6 per /64), over everything that checks a one-time link — `/Invite` and `/ResetPassword` (GET and POST), `POST /api/identity/invites/lookup` and `/accept`, `POST /api/identity/password/lookup` and `/reset`: `PermitLimit` (10) per `WindowSeconds` (60) → `429`. Separate from the sign-in window, so link unfurlers and crawlers opening emailed links cannot spend it |
| `Skanyxx:HealthCheckTimeoutSeconds` | Per-check timeout for `/health` (5). `/health` is anonymous and lists each check's name and status; no exception text, no CORS |
| `KAgent` | KAgent API connection (BaseUrl, Port, Protocol, Token), `ControlTimeoutSeconds` (10; every agents/sessions/ModelConfig call, including the owner's `/` probe), `ChatTimeoutSeconds` (300; one blocking chat turn waits for the whole answer), `ModelConfig` (`kagent/default-model-config`; the one the owner's model step edits). No kagent call is ever retried: a chat turn and a model save are not idempotent |
| `Kubernetes` | Optional kubeconfig path |
| `AWS` | AWS profile and region for cloud tools |
| `Azure` | Azure config directory |
| `Modules` | Plugin directory and enable/disable flags |
| `ConnectionStrings:Identity` | Postgres for accounts, roles and the Data Protection key ring (required). **Use a separate database role (or database) from the other modules** — whoever can read the key ring can mint sessions |
| `Identity` | `BootstrapToken` (`""`; needed outside Development to create or unlock the owner — empty does **not** stop startup, but outside Development `/Setup` and bootstrap answer `403`, unlock `404`, and there is no break-glass sign-in; a malformed token (under 32 characters, or with leading or trailing whitespace) stops startup; also guards `unlock` and the owner's break-glass sign-in — see Identity below), `PasswordMinLength` (12, at least 12), `LockoutMaxFailedAttempts` (5), `LockoutMinutes` (15), `SessionDays` (`7, 1–90`; absolute cap on a cookie session and on a refresh-token chain), `DataProtectionCertificatePath` / `DataProtectionCertificatePassword` (`""`; a certificate that encrypts the key ring at rest; a warning is logged outside Development when unset), `MaxPoolSize` (20), `InviteDays` (7, 1–30), `PublicBaseUrl` (where people reach Skanyxx, e.g. `https://skanyxx.example.com`; invite links are built on it. Optional for startup: **unset outside Development, the app starts and logs a warning, and creating an invite is refused (`409`) until it is set**; Development without it uses the request's scheme and host. When set it must be written exactly — no leading or trailing spaces, backslashes, query, fragment or user info — and absolute `https://`, except a loopback host such as the desktop installs' `http://localhost:5282`, whose links never leave the machine; anything else stops startup. The template ships `http://localhost:5282`, so a server install must change it: a loopback value while Skanyxx listens on a non-loopback address is warned about at startup, and the People page shows the host each new link points at). Validated at startup. (`SecurityStampValidationSeconds` is gone: cookies are checked on every request, and the key is ignored.) `PasswordResetMinutes` (60, 5–1440; how long a reset link works), `AuditRetentionDays` (365, 1–3650; audit rows and finished invites older than this are deleted), `EntraRecheckMinutes` (60, 5–1440; the Graph re-check interval while Microsoft sign-in is on), `Smtp` (outgoing email; `Host` empty = off — see "Email" below) |
| `ConnectionStrings:Memory` | Postgres for the memory engine (required; startup fails without it) |
| `Memory` | `SearchTopK` (5), `UpsertsPerMinute` (30, per caller), `UpsertsPerMinuteTotal` (300, all callers), `MaxPoolSize` (40), `McpPort` (unset; set: `/mcp/memory` is served on that Kestrel port only and is absent from every other, and every other path answers 404 on it, as the Helm chart does with 8081 — the Host must also listen there, e.g. `ASPNETCORE_HTTP_PORTS=8080;8081`). Supervisors (write/lift into `company`, set agent grants) are users with role `owner` or `supervisor` |
| `ConnectionStrings:Tickets` | Postgres for ticket pipelines (required; may be the same database as memory) |
| `Tickets` | `Source` (`jira` \| `local`), `LocalPath` (JSON file, required when `local`), `Jira` (`Site` — https only, `Email`, `ApiToken`, `Project` or `Jql` (overrides `Project`), `TimeoutSeconds` (20)), `KAgentUserId` (`skanyxx-tickets`; the kagent user that owns every stage session), `StageTimeoutSeconds` (600), `PollSeconds` (5), `MaxTicketChars` (20000), `MaxPromptChars` (96000), `MaxAnswerChars` (100000), `MaxStageAttemptsPerRun` (30), `MaxActiveRunsPerUser` (5), `MaxActiveRuns` (20, all users), `MaxPoolSize` (40), `AllowedAgents` (`namespace/name`; empty = the five ticket agents; a list replaces that default). Editing and deleting pipelines needs role `owner` or `supervisor` |
| `Sandboxes` | `Address` (AX gRPC, h2c; `http://ax-server.ax-system.svc:8080`), `Atespace` (`default`), `AllowedImages` (`[]`; registry/repo prefixes, empty = nothing runs, a configured list replaces the default), `RequireDigest` (`false`; on = image must be `@sha256:`), `MaxCpu` (`4`), `MaxMemory` (`8Gi`), `DefaultCpuRequest` (`250m`), `DefaultMemoryRequest` (`256Mi`), `DefaultCpuLimit` (`1`), `DefaultMemoryLimit` (`1Gi`), `MaxActiveTasksPerUser` (`3`), `MaxActiveTasks` (`50`, whole atespace), `CountBudgetSeconds` (`30`), `NetworkIsolationConfirmed` (`false`; must be `true` for a non-empty `AllowedImages`), `MemoryMcpUrl` (`""`; a non-empty value is **refused at startup** until a sandbox-facing listener exists, see AX Tasks), `TimeoutSeconds` (`15`), `WatchSeconds` (`120`), `KeepAliveSeconds` (`15`), `MaxWatchesPerUser` (`2`), `MaxWatches` (`50`) |

## API Endpoints

Each module exposes REST endpoints under `/api/`:

| Module | Routes |
|---|---|
| Agents | `/api/agents` |
| Alerts | `/api/alerts` |
| Analytics | `/api/analytics` |
| Chat | `/api/chat`, `/api/chat/agents` (merged agents only) |
| CloudTools | `/api/cloud`, `/api/cloudtools` |
| Dashboard | `/api/dashboard` |
| Debug | `/api/debug` |
| Hooks | `/api/hooks` |
| Identity | `/api/identity/status`, `/bootstrap`, `/sign-in`, `/refresh`, `/sign-out`, `/me`, `/unlock`, `/invites`, `/people`, `/org/departments`, `/org/teams` |
| Investigate | `/api/investigate` |
| Memory | `/api/memory/cards`, `/api/memory/grants`, `/api/memory/agents/{agentId}/secret` (+ MCP at `/mcp/memory`, agent secret) |
| Tickets | `/api/tickets/issues`, `/api/tickets/pipelines`, `/api/tickets/runs` |
| Sandboxes (experimental) | `/api/sandboxes/tasks`, `/api/sandboxes/workspaces`, `/api/sandboxes/models` |
| Sessions | `/api/sessions` |
| Settings | `/api/settings` (owner only, except `GET /api/settings/theme`; connection tokens are write-only), `/api/model` (owner: kagent ModelConfig) |
| ToolServers | `/api/toolservers` |

Health check: `GET /health` (anonymous; overall status plus each check's name and status, e.g.
`identity-postgres: Healthy`; no exception text)

## Identity and security model

Every page and API requires a signed-in user (fallback authorization policy). Local accounts live in Postgres
(ASP.NET Core Identity, `Skanyxx.Module.Identity`); there is no bundled IdP.

**First run — the owner (D025).** Until an account exists, `/Login` sends you to `/Setup`, which creates the
**owner** (role `owner`, also treated as supervisor — D024) and signs you in.
`POST /api/identity/bootstrap {email, password, displayName?}` does the same for scripts. It works only while no
account exists (serialized under a Postgres advisory lock, so parallel calls create exactly one owner; afterwards
`409`). Deleting every account at the database level reopens it. Who may call it:

- **Outside Development:** only whoever sends `Identity:BootstrapToken` in `X-Bootstrap-Token` (setup page: the token
  field). The token is **required** there: without it the bootstrap API answers `403` naming `Identity:BootstrapToken`,
  and `/Setup` shows the form again with that error (startup still succeeds). A configured token shorter than 32
  characters, or with leading or trailing whitespace, stops startup. It is compared in constant time.
  Installers run the app as Production: the Linux (`.deb`) and macOS (`.pkg`) installers write a random 48-character
  token into the new `appsettings.json` (printed once by the `.deb`; not printed by the macOS installer, whose log is
  world-readable) — it stays there under `Identity:BootstrapToken`. The new file is `0600 root` on Linux (the systemd
  service runs as root) and `0640 root:admin` on macOS (the LaunchAgent runs as whoever logs in). If the token cannot
  be written the install fails rather than print a token the app does not have. An **upgrade** keeps the existing
  `appsettings.json` untouched: if it has no token, the installer says so and you set one yourself. The Windows
  installer does not generate one: set one yourself before first use. An upgraded `appsettings.json` from before
  invites also has no `Identity:PublicBaseUrl`: the app still starts (with a warning), but inviting people is refused
  until you add it — `"PublicBaseUrl": "http://localhost:5282"` on a desktop install, your public `https://` address
  behind a proxy. The Linux and macOS installers say so on upgrade, the Windows installer on its last page.
- **Development only:** with no token set, a direct loopback connection may bootstrap: a loopback peer, no
  `X-Forwarded-For` / `Forwarded`, and a `Host` of `localhost` or a loopback IP. The last rule stops a DNS-rebound
  page in the developer's browser (loopback peer, attacker's host name) even when `AllowedHosts` is `*`.

Why not loopback in production: in Kubernetes, remote traffic very often reaches the app *from* loopback without a
forwarding header — mesh sidecars (Istio connects from 127.0.0.6, Linkerd from localhost), `kubectl port-forward`,
SSH tunnels, socat, oauth2-proxy sidecars, and nginx set up with only `X-Real-IP`. Under a loopback rule, the first
remote visitor to a fresh install behind any of those would become owner.

**Break-glass (the owner is never locked out for good).** Anyone who knows the owner's email can keep the account
locked by sending wrong passwords. Two token-guarded ways back in (both need `Identity:BootstrapToken` set):

- **Sign in with the token** — the one that works while an attacker keeps the account locked:
  `POST /api/identity/sign-in` with the header `X-Bootstrap-Token: <token>`, or the "Owner locked out?" token field
  on `/Login` (at most 512 characters). For the **owner** with the right token, the lockout is not applied; the
  password is still checked, and a wrong one is added to the failed count — which only matters for sign-ins
  **without** the token, so a token holder can keep guessing, bounded by the sign-in rate limit alone. A wrong token,
  or any account other than the owner, gets the ordinary sign-in (lockout applies). Every sign-in that carries a
  token — right or wrong, success or failure — is logged as a warning with the client address.
- **Unlock** — `POST /api/identity/unlock` (body `{"email": "…"}` + header `X-Bootstrap-Token`; `204` unlocked, `401`
  token missing/wrong, `404` no token configured or not an owner, `400` bad email) clears the owner's lockout and
  failed-attempt count. Against a sustained guesser it is relocked within seconds, so prefer the token sign-in there
  and block the source at the ingress.

Both are anonymous, rate limited like sign-in, and do nothing without the token. This is the "break-glass owner
always works" rule of `docs/design/identity.md` (D081, D082).

**Sign-in.** `POST /api/identity/sign-in {email, password, useCookie}`:

- `useCookie: true` (browser; also the `/Login` form) → session cookie `skanyxx.auth`: HttpOnly, SameSite=Lax,
  Secure outside Development, 8 h sliding. The security stamp is checked on every request (one primary-key read,
  as for bearer tokens), and the session ends `Identity:SessionDays` after sign-in no matter how active it is.
- `useCookie: false` (API, desktop — D059) → `{tokens: {accessToken, expiresIn, refreshToken}}`; send
  `Authorization: Bearer <accessToken>` (valid 1 h, never past the chain cap below; `expiresIn` says how long).
  `POST /api/identity/refresh {refreshToken}` returns a new pair.
  **Refresh tokens are single-use**: the one you sent is spent, and presenting it again is rejected (reuse returns `401` and revokes the whole chain, including the newest token). The chain is capped: however often you refresh, it ends `Identity:SessionDays`
  after the original sign-in (each refresh token lives min(14 days, the chain cap)). A request with an `Authorization`
  header is authenticated by the bearer scheme only, never by the cookie.
- **Clients must serialize refreshes: one refresh in flight per refresh token.** Two refreshes sent with the same
  token look exactly like a stolen copy being replayed, so one gets the new pair, the other `401`, and the chain is
  revoked — the winner's new tokens included, so the user must sign in again. Typical triggers: several requests
  that get `401` at once each refreshing, a retry after a lost refresh response, two windows sharing one stored
  token. Keep one refresh task and let every caller await it (pinned by
  `SessionTests.ParallelRefresh_WithOneToken_RevokesTheChain`).
- Failures are one answer — `401 "Invalid email or password."` — for an unknown email, a wrong password and a locked
  account, with the same password-hashing cost. 5 failures lock the account for 15 minutes. Attempts are serialized
  per account without queueing: an attempt that arrives while another is being checked for the same email gets
  `429 "Sign-in is in progress for this account; try again."` with `Retry-After: 1` at once, unchecked and uncounted
  (a double-click, a retry; `/Login` also disables its button after the first click). Unknown emails take the same
  lock, so this reveals nothing about whether an account exists. The lock key is a 64-bit hash of the normalized
  email, so an attacker cannot compute a different email that shares a victim's lock. So parallel guesses cannot
  slip past the lockout, and a flood on one email cannot tie up the identity database pool.
- **Accepted timing difference:** an existing email with a wrong password answers about 4 ms slower than an unknown
  email (the failed-attempt write). Telling the two apart takes dozens of requests under the sign-in rate limit; it
  will be revisited when invites add users.
- `POST /api/identity/sign-out` (cookie or bearer) rotates the user's security stamp and clears the cookie. That
  ends **every** session of that user on every device: all refresh tokens at once, all browser cookies from their next
  request. Access tokens already issued are refused from their next request: the bearer
  handler checks the security stamp too (D087; one primary-key read per bearer request). If the
  rotation cannot be saved, sign-out fails loudly (`500` ProblemDetails, never `204`) instead of reporting success — retry it.
- `GET /api/identity/me` → `{id, email, displayName, roles}`. The nav shows the signed-in user and a Sign out button.
- Rate limits: credential posts (sign-in, bootstrap, unlock, `/Login`, `/Setup`, the Account page's Microsoft link
  — its password check, `POST /Account?handler=LinkMicrosoft` only —, the Microsoft callback `/signin-oidc`, and "forgot your
  password?") have their own window (`Skanyxx:SignInRateLimit`).
  Starting "Sign in with Microsoft" (`POST /Login?handler=Microsoft`) checks nothing and is not counted, so one Microsoft
  sign-in spends one permit, at its callback; sign-out and refresh do not count against it, so a client can always sign out. Invite
  and password-reset lookup and accept, and the `/Invite` and `/ResetPassword` pages (GET too), have another
  (`Skanyxx:InviteRateLimit`), so crawlers opening emailed links cannot spend the sign-in window. All windows are per client address, with IPv6 grouped per /64 (one
  host usually owns a whole /64); behind a proxy that is the proxy's address, so everyone shares one window.
- Unauthenticated API calls get `401` ProblemDetails (never a redirect); pages redirect to `/Login`. Anonymous:
  `/health`, static files, `/Login` (the Microsoft sign-in too), `/signin-oidc`, `/Setup`, `/Invite`, `/ForgotPassword`, `/ResetPassword`, `/Privacy`, `/Error`, `/Offline`, `GET /api/identity/status`, the
  bootstrap/sign-in/refresh/unlock endpoints, `POST /api/identity/invites/{lookup,accept}` and `POST /api/identity/password/{forgot,lookup,reset}`. `/mcp/memory` needs no sign-in but an agent secret (below).
- CSRF: every POST/PUT/PATCH/DELETE on the Host (legacy controllers included) is refused with `403` when its `Origin`
  is not listed in `Skanyxx:AllowedOrigins` (or is the origin of `Identity:PublicBaseUrl`) — except the Microsoft sign-in callback `/signin-oidc`, which Entra posts from its own origin and the OIDC handler protects (below) — (the Host's own origin — Origin host:port equal to the Host header — is allowed implicitly off the guarded prefixes; `/api/{memory,tickets,sandboxes,identity,chat,model}` and `/mcp` still need an allow-listed Origin for any browser caller); the cookie is SameSite=Lax; the Razor forms carry
  antiforgery tokens.

**People, invites and roles (D026 as built: D086, D087).** Only the owner administers people: the **People** page
(nav link shown to the owner only) or the API below; any other role gets `403`, anonymous `401`.

- **Invite:** `POST /api/identity/invites {email, roles}` → `201 {inviteId, link, expiresAt}` (`409` naming
  `Identity:PublicBaseUrl` outside Development while it is unset — nothing is written and no older invite is revoked). `roles` is one or more
  of `supervisor`, `builder`, `employee` (`owner` is never grantable: `400`), and the email must use only the
  characters an account name may (ASCII letters, digits, `-._@+`; `400` otherwise). The link —
  `<Identity:PublicBaseUrl>/Invite?token=skx_inv_…`, never built from the request outside Development
  (256 random bits) — is in this response and on the page that created it **only**: the database keeps its SHA-256,
  the list never shows it, and it is not logged. **With SMTP configured (Email, below) the link is emailed to the person
  and not returned** (`link: null, emailed: true`, D151); without it — or if sending fails — you get it once
  (`emailed: false`) and send it yourself. It is single-use and
  expires after `Identity:InviteDays` (7). An email that already has an account gets `409`; inviting an email again
  revokes its earlier open invite. `GET /api/identity/invites` lists pending invites (no tokens);
  `DELETE /api/identity/invites/{id}` revokes one (`204`, or `404` if no longer pending).
- **Accept:** the invitee opens the link (`/Invite`), sees the email and roles, sets a password (≥ 12 characters and
  not containing the part of the email before the `@` when that part is 3+ characters — a rule for every password
  Identity sets, the owner's at bootstrap too; there is no breached-password list, an accepted risk: D088) and an
  optional display name (no control or invisible formatting characters such as bidi overrides), and is signed in. Scripts: `POST /api/identity/invites/lookup {token}` →
  `{email, roles, expiresAt}` and `POST /api/identity/invites/accept {token, password, displayName?, useCookie}` →
  `201` with the sign-in body (cookie or tokens). Unknown, used, revoked and expired tokens all get the same `404`.
  Accepting spends the invite atomically (parallel accepts create exactly one account); a rejected password leaves
  the link usable. The token travels in the query string or body, never a URL path, so request logs and traces do
  not record it (a reverse proxy's access log may record query strings — the link is single-use and short-lived);
  every answer of the page sends `Cache-Control: no-store` and `Referrer-Policy: no-referrer` (`404` invalid link,
  `409` the email has an account by now, `400` a rejected password or display name). Only a pending invite takes the
  email's account lock, so a spent link cannot make the person's sign-ins busy.
- **People:** `GET /api/identity/people` → `[{id, email, displayName, roles, disabled}]`;
  `PUT /api/identity/people/{id}/roles {roles}` replaces the grantable roles; the owner's roles are fixed (`403`: the
  owner already has every permission a role grants, and the page shows no role boxes on the owner's row);
  `POST /api/identity/people/{id}/disable` and `/enable`. Disabled accounts get the ordinary
  `401 "Invalid email or password."` at sign-in. The owner account cannot be disabled or enabled (`403`).
- **Immediate effect:** a role change (only when the roles actually change) or a disable rotates the person's
  security stamp and drops their refresh chains: bearer access tokens, refresh tokens and browser cookies are all
  refused from their next request (both carriers check the stamp on every request, D089). The person signs in again
  and gets the new roles. A disable that lands while that person's sign-in is between its password check and issuing
  the cookie hands out a cookie carrying the old stamp, which its first request refuses.
- **Offboarding — agent secrets (D088, D089):** saving someone's roles without `supervisor`, or disabling them, also
  deletes every memory agent secret they issued (`memory_agent_secrets.created_by`), after the change commits: those
  agents get `401` on `/mcp/memory` until a supervisor issues new secrets (and the new secret goes into the agent's
  k8s Secret). Each revoked agent is logged at Warning. Secrets the owner issued (including acts-for-users ones) are
  the owner's and stay. Identity does not touch the memory database: it publishes `PrivilegesRevoked` and the memory
  module revokes; the step runs to the end even if the owner's browser disconnects. If it fails, the change itself
  is already saved, the call answers `500` and an Error is logged naming the person and the actor: **save the same
  roles (or disable) again** — that re-runs the revocation, and it is harmless when there is nothing left to revoke.
  By hand: `GET /api/memory/agents/{agentId}/secret` shows `createdBy`, and `DELETE` on the same path revokes. Enabling
  someone again does not restore anything. **Disabling also stops the person's active sandbox tasks** (D154; with
  sandboxes on): their Pending, Running and Suspended AX tasks are deleted, each logged at Warning; workspaces stay, and
  losing `supervisor` alone stops nothing. If AX cannot be reached or a task will not stop, the disable is saved, the
  call answers `500` with `detail` "Account disabled. Its sandbox tasks could not be stopped (AX unavailable) — disable
  again to retry." (the People page shows the same line, D162; Error log) and disabling again retries. A task someone
  else deleted meanwhile counts as stopped (D163). Every handler of the announcement runs even when another fails
  (D153), and memory's revocation runs first (D160), so an AX outage never keeps memory from revoking the secrets. A
  disable also revokes the person's open password-reset links (D165).
- **Audit:** invite created/revoked/accepted, role change and disable/enable are logged at Warning with the actor
  and target ids and the client address (the connection's, as for agent secrets: the proxy behind one), never a
  token or password. Refused invite accepts and lookups are logged at Warning too (the invite id when one was found,
  otherwise just "invalid"), and so is a signed-in person holding none of an owner-only route's roles (actor, method, route template —
  not the path, which the caller writes). A failed revocation after a role change or disable is logged at Error.
  Commands that carry a token or password print them as `***`. Each of these lines also writes a durable audit row
  (below).

**Audit trail (D152, D155).** Every identity change and refusal that is logged at Warning — invites, roles,
disable/enable, removed Microsoft logins, the org tree, Microsoft sign-in settings and the accounts Entra creates,
re-maps, links or refuses, refused managed passwords and refreshes, bootstrap-token sign-ins, owner-route refusals,
password resets, the Graph re-check — is also a row in `identity_audit`: when, action, actor, target, client address and
JSON details, **never a token, password or secret** (invited email addresses do appear). The row is written in the same
transaction as the change, so a change that fails leaves no row; a refusal changes nothing and its row stands alone.
Refusals anyone can cause without an account — unknown "forgot" emails, invalid link lookups and accepts, failed
Microsoft callbacks, owner-route refusals (per route and person) — are written **at most once a minute per kind on each replica**, the row's
`skippedBefore` counting the ones left out since the previous row (the Warning line is still written for every one,
D167). Rows cannot be updated or truncated (database triggers refuse both, D169; only the database owner can switch them
off). The Audit page's address column is the **direct peer** of the connection: behind an ingress it is the proxy. Only the owner reads them: the **Audit** page (owner nav; filter
by action or person, "Older" pages) or `GET /api/identity/audit?before=<id>&limit=<1–200, 50>&action=<action>&userId=<id>`
→ `[{id, at, action, actorId, targetId, remoteIp, details}]`, newest first (`userId` matches actor or target; others
`403`, anonymous `401`). **Retention:** a job (one minute after start, then every 6 h, on each replica) deletes rows older
than `Identity:AuditRetentionDays` (365), invites that stopped being pending (accepted, revoked or expired) before then,
and reset links a day after they expired, in batches of 5,000 (D169), and writes an `audit.retention_purged` row with the
counts.

**Email (D150).** Optional. Set `Identity:Smtp` and Skanyxx emails invite links (above) and password-reset links
(below); leave `Host` empty and nothing changes (no email, no "Forgot your password?"). Settings: `Host`, `Port` (587),
`Security` — `StartTls` (default, STARTTLS must succeed), `SslOnConnect` (port 465), or `None` (**only for a loopback
host**, such as a local relay or mailpit: anything else would send one-time links in clear text, and startup refuses
it) —, `UserName` + `Password` (both or neither; put the password in the environment, `Identity__Smtp__Password`, or a
Secret, never in a committed file), `From` (the sender address, required), `FromName` (`Skanyxx`), `TimeoutSeconds`
(15). Unusable settings stop startup with the reason (never the password). Mail needs `Identity:PublicBaseUrl` for its
links (a warning at startup otherwise). Messages are plain text and say what the link is for, the link, when it expires
and that it works once. A send is capped at twice `TimeoutSeconds` in all and stops when the owner's request goes away
(D168). A failed send is one Warning (server, port, error type — never recipient, body or credentials): the owner's
flows then show the link instead, with an audit row saying so (`invite.link_shown` / `password.reset_link_shown`); a
self-service reset just sends nothing. Office 365 / Gmail: STARTTLS on
587 with an app password or a relay connector. Implemented with MailKit (`System.Net.Mail.SmtpClient` is not
recommended for new code).

**Password reset (D156, D157).** Two ways, both ending on `/ResetPassword?token=skx_rst_…` (256 random bits, stored as
SHA-256 only, single use, valid `Identity:PasswordResetMinutes` (60); a newer link for the account revokes the older):

- **"Forgot your password?"** on `/Login` (shown only with SMTP) → `/ForgotPassword`, or
  `POST /api/identity/password/forgot {email}` → always `202` with the same body, whatever the email: nothing is looked
  up before the answer. A background worker then emails a link only to an account that may reset and has not been sent
  one in the last two minutes; everything else gets nothing (and an audit row). Without SMTP it is `404` "Password reset
  by email is not available here. Ask the owner for a reset link." Counted in the sign-in window.
- **The owner's link:** People → "Password reset link", or `POST /api/identity/people/{id}/password-reset` (owner only)
  → `201 {link, expiresAt, emailed}`: emailed to the person with SMTP (`link: null`), otherwise shown once
  (`Cache-Control: no-store`) for you to pass on. Issuing changes nothing else; the old password works until the link is used.
- **Using the link:** the page shows the account's email and takes the new password (the usual rules); scripts:
  `POST /api/identity/password/lookup {token}` → `{email, expiresAt}` and `POST /api/identity/password/reset {token,
  password}` → `204`. The password is replaced, the lockout and failed count cleared, every other open link revoked and
  **every session ended** (cookies, bearer and refresh tokens); no session is started — sign in with the new password.
  Unknown, used, revoked and expired links are one `404`; a rejected password leaves the link usable (`400`). Both the
  page and the API answer `no-store`, the page also `no-referrer`; both count in the one-time-link window.
- **Who cannot reset:** the **owner** (`403`; break-glass is still the bootstrap token), a **disabled** account (`409`;
  enable it first — a disable revokes every open link, and a link refused at use is revoked too, so enabling the
  account never brings an old link back, D165), and an account that **signs in with Microsoft
  while Microsoft sign-in is on** (`409`). With Microsoft sign-in off, such an account can reset — which is how an
  account Entra created gets a password.

**Teams and departments (D055 as built: D090).** The org tree is Skanyxx's: **departments** contain **teams**, and
people are members of teams (none, one or several); a person is in a department through its teams. Only the owner
manages it — the **Org** page (nav link for the owner) or the API below; any other role gets `403`, anonymous `401`.

- **Departments:** `POST /api/identity/org/departments {slug, name}` → `201`; `GET /api/identity/org/departments` →
  `[{slug, name}]`; `PUT /api/identity/org/departments/{slug} {name}` renames.
- **Teams:** `POST /api/identity/org/teams {slug, name, department}` → `201`; `GET /api/identity/org/teams` →
  `[{slug, name, department, members: [userId…]}]`; `PUT /api/identity/org/teams/{slug} {name, department}` renames
  and/or moves the team to another department (its members' department membership moves with it at once).
- **Members:** `PUT /api/identity/org/teams/{slug}/members/{userId}` adds an existing, **enabled** person (`409` when
  disabled, `404` unknown person or team); `DELETE` on the same path removes. Both are idempotent and answer the team.
  Disabling someone keeps their memberships (they cannot sign in; the Org page marks them *disabled*) and enabling them
  restores those memberships — remove them first if that is not wanted.
  `GET /api/identity/people/{id}/teams` lists a person's teams; the People page shows them.
- **Slugs** are the memory scope ids (`team:<slug>`, `department:<slug>`): 1–128 characters of `a-z 0-9 . _ @ -`,
  starting with a letter or digit (`400` otherwise; the table has the same CHECK). Unique per kind (a taken slug is
  `409`; a team and a department may share one), **never changed** (a slug in a rename body is ignored — the route's
  wins) and never reused (nothing is deleted in this slice). Names follow the display-name rules (≤ 100, no
  control or invisible formatting characters).
- **Audit:** department created/renamed, team created/renamed/moved and member added/removed are logged at Warning with
  the actor, the target and the client address; refusals on these routes are owner-route refusals (above).

**Memory follows the tree on every request.** The memory module asks identity through Core's `IOrgMembership`
(in-process, once per request, never cached beyond it; nothing about teams is in cookies or tokens), so adding,
removing or moving takes effect on the person's **next request**. Memory therefore needs the identity module: the Host
refuses to start with `Modules:Enabled:identity=false` while memory is enabled. Rules for signed-in people:

| Scope | Read / search | Write (upsert, or lift into) |
|---|---|---|
| `company` | everyone signed in | owner, supervisors |
| `personal:<you>` | you | you |
| `team:<slug>` | members; owner, supervisors | members only |
| `department:<slug>` | members of its teams; owner, supervisors | members of its teams only |

Owner and supervisors read and search every team and department (oversight) but write only where they are members —
add yourself to the team to write there. A lift also needs to read its source. Cards in a `team:`/`department:` scope
with **no matching org object** (written before the tree existed, or a typo) have no members, so only owner and
supervisors read them until the owner creates that slug (then its members see them too). **Agents** stay on grants
(`PUT /api/memory/grants/{agentId}`). An agent acting for a user gets the user's personal scope, never the user's teams;
grant `team:<slug>` explicitly, and remember that everyone who talks to that agent then reaches that team's cards.
**Team and department grants are the owner's** (search or upsert, D091): a supervisor's grant list containing one is
`403` and nothing is written, and a supervisor may neither replace the grants of nor issue a secret for an agent that
holds one (supervisors keep `company` and `personal` grants). The owner's own team or department grant is `409`
("Rotate this agent's secret first…", nothing written) while the agent's secret was issued by someone else, who has
seen it (D092): rotate the secret as the owner, then grant. A grant row with both flags off opens nothing and does not
count as a team grant. A person refused a write is told who may write there
("Only members of team:x may write there."); an agent still hears "No upsert grant".

**Audit (D091).** Every grant change and refusal is logged at Warning with actor, agent, each scope with its flags and
the client address. A supervisor may lift a team or department card they are not a member of into `company`
(supervisors gate company); every such lift is logged at Warning with actor, source, target, key and client address.

**Upgrading from before teams (D091, D092).** Nothing written before this release is re-checked: cards already in a
`team:`/`department:` scope become readable by that team's members once the owner creates the slug, and team or
department **grants** a supervisor set back then — and the secrets supervisors issued for those agents — keep working.
Before creating teams on an upgraded install, the owner should list `team:`/`department:` scopes in `memory_cards`,
review every agent's grants (`GET /api/memory/grants/{agentId}`) and rotate the secret of any agent with a team grant
whose secret someone else issued. Only development data predates this release today.

**Microsoft Entra ID sign-in (D027 as built: D093–D095).** Optional: people sign in with their Microsoft work account
("Sign in with Microsoft" on `/Login`), and the groups they are in decide their roles and teams. Entra is only an
external login: Skanyxx still issues its own cookie (same stamp checks, same session cap), and nothing needs Entra —
invites and passwords keep working. Only the owner configures it, on the **Microsoft sign-in** page (owner nav) or
`GET/PUT /api/identity/entra/settings`; a save applies to the next sign-in, no restart.

Setup, once, in the Entra admin center:

1. **App registration** → New: *Accounts in this organizational directory only* (single tenant); platform **Web**;
   redirect URI **`<Identity:PublicBaseUrl>/signin-oidc`** exactly (the Microsoft sign-in page shows it). It is built
   on `Identity:PublicBaseUrl`, never on the request, so the setting is required outside Development (turning sign-in
   on without it is `409`). Leave implicit grant (ID tokens / access tokens) off.
2. **Certificates & secrets** → a client secret; paste it on the page. It is write-only: stored encrypted with the
   Data Protection key ring (below), never shown again, returned or logged (the page says only whether one is set;
   leaving the field empty keeps it). Entra secrets expire — enter a new one before that. If the key that protected
   it is deleted (the key-ring runbook below), sign-in turns itself off with an Error log until the owner enters the
   secret again. Outside Development without `Identity:DataProtectionCertificatePath`, the keys sit unencrypted in the
   same database as the protected secret, so a copy of that database yields the secret: the page and the API
   (`secretKeysUnencrypted`) warn and the save logs a Warning — configure the certificate.
3. **Token configuration** → *Add groups claim*: Security groups (or *Groups assigned to the application*, which needs
   Entra ID P1 and counts direct members only), emitted as **Group ID** in the ID token. *Add optional claim* (ID
   token): **`email`** and **`acct`** (and, if you like, `xms_edov`). **`acct` is required:** it is how Skanyxx tells a
   guest (`acct = 1`) from a member (`0`), so a token without it is refused ("…the owner must add the optional claim
   'acct'…", Error log) and nobody gets in until it is added. **Map only security groups** — ideally role-assignable
   ones, whose membership only admins change — never Microsoft 365 groups people can join themselves, nor dynamic
   groups built on attributes users can edit: whoever can change a mapped group's members decides Skanyxx roles,
   `supervisor` included.
4. **Enterprise applications** → this app → Properties → **Assignment required: Yes**, and assign the mapped groups:
   people outside them are stopped by Entra before Skanyxx sees them.
5. **Group overage** (someone in more than 200 groups): API permissions → Microsoft Graph → **Application**
   permission **`GroupMember.Read.All`** → **Grant admin consent**. The token then says "too many groups" instead of
   listing them, and Skanyxx asks Graph `POST /users/{oid}/checkMemberGroups` — app-only, with the same client id and
   secret, about the **mapped** group ids only, at most 20 per call — and never follows the token's `_claim_sources`
   link. Without the permission such a person gets `502` "could not read your groups" and nothing changes.
6. On **Microsoft sign-in**: directory (tenant) id, application (client) id, the secret, and the **group map** — each
   row a group's **object id** (GUID; the label is only for you) → roles among `supervisor`, `builder`, `employee`
   (never `owner`) and/or teams (existing ones). Tick *Offer "Sign in with Microsoft"* and save. With it off, `/Login`
   shows no button and starting a Microsoft sign-in or link is `404`.

What a Microsoft sign-in does:

- **Who gets in:** a member (not a guest) of the configured tenant (`tid` is checked as well as the issuer) who is in
  **at least one mapped group**. A guest is `acct = 1`, or any token with an `idp` claim that is not exactly this
  tenant's issuer (`https://sts.windows.net/<tid>/` or `https://login.microsoftonline.com/<tid>/v2.0`) — another
  tenant, a personal Microsoft account, a federated partner — whatever `acct` says. Everyone else is refused ("…has no access to Skanyxx"), and no
  account is made. A token without `acct` is refused and changes nothing (the person is not at fault).
- **Which account:** the one whose Microsoft login is `tid|oid` — never matched by email. The first sign-in creates
  it (email and display name from the token; no password) — unless `xms_edov` is present and false (the tenant does
  not vouch for the email's domain): then no account is made (`403`), and the person asks for an invite and links. If
  the email already belongs to an account, the sign-in is refused (`409`): that person signs in with the password and
  links Microsoft on their **Account** page (header → their name), **re-entering their current password** (the same
  lockout rules as a sign-in; a session cookie alone cannot link). A link is accepted only for the signed-in user who
  started it and only with a mapped group; it ends the account's other sessions.
- **Password sign-in stops for managed accounts:** while the owner has Microsoft sign-in turned on, an account with a
  Microsoft login (other than the owner) gets no session from its password: the answer is the one a wrong password
  gets (`401` "Invalid email or password."), right password or not, so it tells a guesser nothing; a wrong one still
  counts toward the lockout and a right one resets nothing (it is logged at Warning). The Login page says, for
  everyone, "If your organisation uses Microsoft sign-in, use the button below." Its refresh tokens are refused (`403`,
  Warning with account and address). So a removal from the mapped groups reaches it at its next session. This follows
  the owner's switch, not whether sign-in currently works: a stored secret that can no longer be decrypted turns
  Microsoft sign-in off but keeps the passwords refused. **Turning Microsoft sign-in on** (off → on) ends every session
  of every managed account except the owner's (stamps rotated, refresh chains dropped; one Warning with the count), so
  a password session opened while it was off does not outlive the switch. Turn it off and the password (if the account
  has one) and refresh tokens work again.
- **Managed by Entra:** an account with a Microsoft login gets, at **every** Microsoft sign-in, exactly the roles and
  teams its groups map to (the union over its mapped groups); changes made on People or Org last until then, and both
  pages mark such accounts *managed by Entra*. A role change ends the account's other sessions. A sign-in or link that
  takes `supervisor` away revokes the agent secrets the account issued (`PrivilegesRevoked`). The revocation is marked
  as owed in the same transaction and cleared only once it has been published, so if revoking fails the sign-in is a
  `500` with an Error log and the account's next Microsoft sign-in publishes it again. A sign-in that takes nothing
  away, and owes nothing, does not touch the memory database at all. People role saves and disables mark it the same
  way, so a revocation that failed there is also retried by the next Microsoft sign-in. A **refused** sign-in of an
  existing managed account (no mapped group any more, or a guest now) takes all its roles and teams, ends every session
  and revokes as above; the account stays. A disabled account is refused and left as it is, and so is an account
  whose Microsoft login the owner removed while the sign-in was in flight.
- **The owner stays local:** the owner cannot link a Microsoft account (the Account page says so; the challenge is
  `403`), and a Microsoft login found on the owner anyway signs nobody in. The mapping never changes the owner.
- **Removing a Microsoft login:** on People, *Remove Microsoft login* (owner only, after a confirmation; `DELETE
  /api/identity/people/{id}/entra-login`) drops the login, ends every session and makes the account local-only: it
  signs in with its password, and its roles and teams stay — review them, they came from the mapping. Audited at
  Warning. It is refused (`409` "Microsoft is this account's only sign-in; disable it instead.") for an account without
  a password, which is every account Microsoft sign-in created: removing its only sign-in could not be undone. People
  shows no button for those; **Disable** stops such an account and can be reversed.
- **Groups are re-checked between sign-ins (D158).** Every `Identity:EntraRecheckMinutes` (60) while Microsoft sign-in
  is on, one replica asks Graph `checkMemberGroups` (the same app-only permission as group overage,
  `GroupMember.Read.All`, the mapped ids only) about every managed account except the owner and disabled ones, and
  applies the answer like a sign-in: re-mapped (a role change ends its sessions), or — **no mapped group left, or the
  user deleted from the tenant — refused**: roles and teams removed, the account kept, a lost `supervisor` revoked, and —
  the first time, even when there was no role or team to remove — every session ended and the person's **sandbox tasks
  stopped** (D161; a failed stop is retried by the next sweep). Back in a mapped group, the refusal is cleared; so is every
  refusal when Microsoft sign-in is turned off (or left with no mapped group) or the tenant changes, and a refusal only
  counts while sign-in is usable — with it off the person signs in with a password again. Graph
  unreachable: nothing changes (a Warning); Graph answering `403` (consent for `GroupMember.Read.All` missing) or `400`
  (a mapped group id Graph rejects — malformed or stale) is an Error naming that cause, every sweep, until fixed. Replicas share one
  schedule (`identity_job_runs`, D164): a sweep runs once per interval across all of them, and a restart does not
  postpone an overdue one. So a removal in Entra reaches a signed-in person
  within the interval; to cut someone off at once, disable the account on People. Guests are not re-checked (that
  needs a token), and accounts whose Microsoft login is from another tenant are skipped (a Warning with the count).
- **Settings API:** `GET /api/identity/entra/settings` →
  `{enabled, tenantId, clientId, clientSecretSet, active, groups: [{groupId, label, roles, teams}], redirectUri,
  updatedBy, updatedAt, secretKeysUnencrypted}` (never the secret; `active` = on and usable on this instance); `PUT` takes `{enabled,
  tenantId, clientId, clientSecret?, groups}` and replaces the whole map (`clientSecret` omitted or empty keeps the
  stored one). Ids are GUIDs; turning it on needs a tenant, a client, a secret and at least one group; each group must
  give a role or a team (`400`); an unknown team is `404`. Saves are serialized across replicas (an advisory lock),
  each gets its own version, and other replicas pick a save up within 30 s.
- **Behind a proxy:** Entra posts the result back to `/signin-oidc` from its own origin (`form_post`), a cross-site
  POST, so the handler's correlation and nonce cookies are `SameSite=None; Secure` and the browser must reach Skanyxx
  over **https** (plain http only on `localhost`). The handler marks them Secure whatever scheme the
  request arrived on (checked by a test), and the redirect URI comes from `Identity:PublicBaseUrl`, so behind a
  TLS-terminating proxy neither depends on forwarded headers; what the proxy must do is serve https to the browser and
  pass `/signin-oidc` through (POST included). If you do configure `UseForwardedHeaders`, trust only your proxies
  (`KnownProxies`/`KnownNetworks`): a spoofed `X-Forwarded-Proto`/`Host` would otherwise change what the app believes
  about the request. The callback is exempt from the origin check
  (the handler checks the state, the correlation cookie, the nonce and PKCE instead) and is the one place a Microsoft
  sign-in counts in the sign-in rate-limit window. Replicas already share the Data Protection key ring, so a callback may land on any of them.
- **Audit:** settings saves (actor, on/off, tenant, client, "secret replaced/kept", the map — never the secret),
  turning sign-in on (with the number of managed accounts whose sessions ended), accounts created, re-mapped, linked and
  refused sign-ins, refused password sign-ins and refreshes of managed accounts, link attempts with a wrong password
  and removed Microsoft logins are logged at Warning with the Entra key (`tid|oid`), the
  account and the client address; a refused callback (wrong tenant, cancelled, replayed) at Warning with its reason.

**Caller identity.** Memory, Tickets and Sandboxes take the caller from the signed-in principal; `X-User-Id` is
ignored on `/api/*`. Supervisor = role `owner` or `supervisor`; the `*:Supervisors` lists are gone (leftover keys are
ignored).

**User ids are GUIDs.** A user id is the account's lowercase GUID everywhere: memory cards' `who`, personal scopes
(`personal:<guid>`), and `CreatedBy` / `DecidedBy` / owner on runs, pipelines and sandbox tasks. kagent must forward
that GUID in `X-User-Id` over MCP to reach a user's personal cards (anything else in that header counts as no user). Data written in development under the old
free-form ids (`personal:ana`, `CreatedBy: ana`) is orphaned; nothing migrates it.

**Data Protection keys** (they sign and encrypt cookies and bearer tokens) live in Postgres
(`identity_data_protection_keys`), shared by replicas. Whoever can read that table — or a backup of it — can mint an
owner session. So:

- give `ConnectionStrings:Identity` its **own database role (or database)**; the template's shared connection string
  is for development only, and a SQL-injection or credential leak in any module on a shared role reaches the keys;
- set `Identity:DataProtectionCertificatePath` (+ `DataProtectionCertificatePassword`) to encrypt the key ring at rest
  with that certificate (e.g. mounted from a Kubernetes Secret). It must be a PFX (PKCS#12) file that includes the
  RSA private key; without it the ring could not be read back after a restart, so startup refuses such a file. Any
  other format (PEM, DER) also stops startup, and so does a PFX beyond the .NET loader's limits (`Pkcs12LoaderLimits.Defaults`, e.g. over 300,000 KDF
  iterations; OpenSSL and Windows exports are far below). Without it the keys are stored unencrypted, and the Host
  logs a warning at startup outside Development.
- **Turning the certificate on later:** the keys already in the table stay unencrypted, and the newest of them keeps
  protecting new cookies and tokens until it nears expiry (default key lifetime 90 days). Anyone who read the table
  or a backup before the switch can still mint sessions until then. So, after the first start with the certificate:
  1. stop every replica;
  2. delete the old keys: `DELETE FROM identity_data_protection_keys WHERE "Xml" NOT LIKE '%<encryptedSecret%';`
     (or revoke them with `IKeyManager.RevokeAllKeys`, which also revokes the encrypted ones);
  3. start again — a new, encrypted key is created at once.

  This signs everyone out (cookies and bearer/refresh tokens protected by the old keys stop working) and invalidates
  open forms' antiforgery tokens. Rotate the identity database role's password too if the table may have been read.
- **This storage choice (D6: key ring in the identity database, unencrypted unless a certificate is set) was made
  during this slice and is awaiting the user's confirmation.**

**`/mcp/memory` takes a per-agent secret (D080).** Agents are not users, so the memory MCP is outside sign-in; instead
every request — `initialize` and `tools/list` included — must carry `Authorization: Bearer <agent secret>`. No header,
another scheme, a user's access token, or an unknown or revoked secret is `401` (`WWW-Authenticate: Bearer`) before MCP
reads the body. The agent is **whoever owns the secret**; `X-Agent-Id` is not read. The server is stateless: with a
valid secret, `GET` (standalone SSE stream) and `DELETE` (session end) answer `405` (`Allow: POST`), as MCP asks, so
a `401` in the log always means a missing or wrong secret.

- **Issue / rotate** (supervisor = role `owner` or `supervisor`; others `403`, anonymous `401`):
  `POST /api/memory/agents/{agentId}/secret`, optional body `{"actsForUsers": true|false}` (default `false`; `true`
  is **owner only**, a supervisor gets `403`) → `{agentId, secret, createdAt, actsForUsers}`. The secret (`skx_mem_` + 32 random
  bytes, base64url) is in this response and **nowhere else, ever** — only its SHA-256 is stored, it is never logged,
  and the response is `Cache-Control: no-store`. Issuing again replaces it: the old secret stops working at once, and the new one gets whatever
  `actsForUsers` this request says (omit it and a rotation turns it off). Issue, rotation and revocation are logged at
  **Warning** with the acting user, the agent, the flag and the client IP — never the secret. Refused attempts
  (`403`: a supervisor asking for `actsForUsers`, or touching an owner's acts-for-users secret; a non-supervisor) are
  logged at Warning too, with the actor, the agent and the reason. The IP is the direct TCP peer: behind an ingress or
  proxy it is the proxy's (Skanyxx does not trust forwarded headers), so the **actor user id** is what attributes the action.
- **An acts-for-users secret is the owner's:** rotating or revoking it is owner only. A supervisor gets `403` and the
  secret keeps working — checked inside the one SQL statement that writes or deletes the row, so a concurrent owner
  issue cannot slip past it. Secrets without the flag stay rotatable and revocable by any supervisor.
- **Status:** `GET …/secret` → `{agentId, hasSecret, createdAt, actsForUsers, createdBy}` (never the secret;
  `createdBy` is the issuing user's id, `null` when there is no secret). **Revoke:** `DELETE …/secret` →
  `204` (`404` if there is none, `403` for a supervisor when the secret acts for users); the agent is locked out until a new secret is issued.
- **The user (`X-User-Id`, D084)** is honoured only when the agent's secret was issued with `actsForUsers: true`,
  and only when it is a user id (lowercase GUID). Otherwise it is ignored: the call has no user, so no personal
  scope — personal search finds nothing personal and personal upsert is refused. **Consequence, stated plainly: an
  agent that acts for users can read and write any user's personal memory just by naming that user's id**; the
  secret is the whole credential and Skanyxx cannot check that the user asked. That is why only the owner can turn
  it on. With it on, the header is exactly as trustworthy as kagent's own authentication — with kagent's Helm default
  `auth.mode: unsecure`, whoever can talk to kagent chooses it, and 0.10.2 has no `secure` mode (`trusted-proxy` takes
  the user from a JWT it does not verify, trusting the proxy in front; D101). **Keep kagent's API cluster-private**
  (or behind an authenticating proxy), forward the header with `allowedHeaders: [x-user-id]`, and don't give such an agent shell or Kubernetes tools.
- **kagent wiring:** one `RemoteMCPServer` per agent carrying that agent's secret from a Kubernetes Secret (the value
  is the whole header, `Bearer <secret>` — kagent adds nothing), so the controller's own `tools/list` discovery
  authenticates too. Example: `deploy/kagent/memory/`.
- **Rotation runbook:** 1. `POST …/agents/{agentId}/secret` (the old secret is dead from here on — do steps 2–3 right
  away); 2. update the Kubernetes Secret with `Bearer <new secret>`; 3. **make kagent re-render the Agent** by
  changing one of its labels, e.g. `kubectl -n kagent label agent <agent> skanyxx.dev/memory-secret=$(date +%s)
  --overwrite`. kagent copies header values into the agent's rendered config and watches no header Secrets, so a
  changed Secret alone reaches nobody — and neither does a plain `rollout restart`, which reloads the same stale
  config. A label change makes the controller reconcile (it watches generation and label changes), the config hash
  changes, and the agent pod rolls by itself. Between steps 1 and 2 the RemoteMCPServer's ~60 s tool refresh gets
  `401`s; expected, and it clears once the new Secret is in place. `deploy/kagent/memory/README.md` has a pipeline
  that stops on a failed issue instead of writing an empty secret.
- **Requirements:** `/mcp` must **not** be on the public ingress (it shares the UI's listener and host name, and a
  leaked secret works from anywhere that can reach it), and kagent → Skanyxx traffic must be encrypted — mesh mTLS,
  or `https://` with the RemoteMCPServer `tls` block. A NetworkPolicy example that admits only the `kagent` namespace
  (and the ingress controller) is in `deploy/kagent/memory/`.

**Still open (accepted for now):**

- **Jira via the service account.** Any signed-in user can read every Jira ticket the configured Jira account can see,
  and every stored prompt (run reports and datasets hold full ticket text).
- **Jira text in kagent.** Stage sessions sit in kagent under the single user `Tickets:KAgentUserId`; whoever can act
  as that user in kagent can read them.
- **Host guards.** `AllowedHosts` must be an explicit list (the Host refuses `*` outside Development); browser clients
  must be listed in `Skanyxx:AllowedOrigins` (the origin of `Identity:PublicBaseUrl` is implied); only the guarded prefixes (`/api/memory|tickets|sandboxes|identity|chat|model`,
  `/mcp`) and the credential posts are rate limited — legacy controllers and pages are not; `/health` lists check names and
  statuses anonymously (no exception text).
- **Behind a proxy** both rate-limit windows see the proxy's address (ForwardedHeaders is not configured), so all
  clients share one sign-in window (the Microsoft callback `/signin-oidc` counts in it too). Configure forwarded headers with known proxies when real ingress lands.
- **Passwords (D7):** at least 12 characters, no composition rules, no breached-password check (chosen during this
  slice; awaiting the user's confirmation).
- **Key ring storage (D6):** Data Protection keys in the identity database, unencrypted unless
  `Identity:DataProtectionCertificatePath` is set (chosen during this slice; awaiting the user's confirmation).
- Not verified against a real cluster or behind a real ingress yet.

**Next slices:** a sandbox-facing memory credential (per-task, so AX sandboxes can attach memory). Built: per-agent
secrets for MCP (D080 → D083), invites and roles (D086–D089), teams and departments (D090–D092), Microsoft Entra ID
sign-in (D093–D095), the .NET 10 upgrade (D097), email, the audit trail, password reset, the Entra re-check and sandbox
stop on disable (D150–D159).
Design: `docs/design/identity.md`, D079–D093; leftovers: `open.md`.

## Memory engine

Company knowledge as small **cards** in Postgres (design: `docs/design/memory-*.md`, D034–D053).

```bash
docker compose up -d                 # Postgres on 127.0.0.1:55432 (schema is migrated at startup)
dotnet run --project src/Skanyxx.Host
dotnet test tests/Skanyxx.Module.Memory.Tests   # needs Docker (Testcontainers)
```

- A card is `scope` + `key` (`personal:ana/refund-window`), `type` (decision | fact | procedure | open), `what` ≤ 200, `why` ≤ 400; `body`/`source` are never sent to agents.
- Write with `PUT /api/memory/cards/{scope}/{key}`: `version: 0` creates, otherwise pass the version you read. A stale version is a `409` with the current card — never last-write-wins.
- `POST …/{scope}/{key}/lift` copies a card up (personal → team → department → company); the original stays. Into `company` needs a supervisor; into a team or department, membership of it (the org tree, see "Teams and departments").
- Team and department scopes follow the org tree: members read, search and write their teams and departments; owner and supervisors read all of them but write only where they are members. Only the owner grants an agent a team or department scope.
- kagent reaches the bank over MCP at `/mcp/memory` (`memory_search`, `memory_upsert`) with a per-agent secret: `POST /api/memory/agents/{agentId}/secret` as a supervisor, then give the agent's `RemoteMCPServer` an `Authorization: Bearer <secret>` header from a Kubernetes Secret (`deploy/kagent/memory/`). Agents without grants search `company` + the calling user's personal scope and may upsert only that personal scope (needs `X-User-Id`, honoured only for an agent whose secret the owner issued with `actsForUsers: true`); `PUT /api/memory/grants/{agentId}` replaces the default with explicit search/upsert per scope (at least one entry; revoke an agent with a single grant that has `canSearch` and `canUpsert` false).
- **Identity:** `/api/memory` acts as the signed-in user; on MCP the agent is the owner of the presented secret and the user is the `X-User-Id` the agent vouches for, if it may act for users (see Identity and security model).

- `POST …/{scope}/{key}/rename {newKey, version}` renames a key (D038), for people only (MCP has no rename): allowed where you may write the scope; a stale version or a key already taken in the scope is a `409` with the current card and `reason` `stale` or `taken`; copies lifted from it keep their link (it is by id). Logged at Warning with the actor. Counts against the write rate limit only when it writes (D104).

The host refuses to start if the memory database is unreachable. If a local database has an older migration history, reset it with `docker compose down -v`.

### Library

The **Library** page (`/Library`, in everyone's nav) is how people see the bank (D009, D048). It is UI over the memory
module: every rule is memory's `AccessPolicy`, reached through the same commands as `/api/memory/cards`
(`Skanyxx.Core.Platform.Memory`), so the page cannot allow what the API refuses.

- **Search and filter:** any word matches (the same full-text index as agents use); the scope filter offers what you may
  read: `company`, your personal scope, your teams and departments (owner/supervisors: every team/department scope
  that holds cards). Without text it lists the newest cards. At most 50 rows.
- **What is listed:** `company` shows **published** cards only; personal, team and department scopes show every status,
  with a badge on `candidate`/`stale`. No folder tree.
- **Open a card:** what, why, who, version, status, body and source (shown as text; nothing is fetched). A lifted copy
  names its source only if you may open it. An unpublished `company` card opens only for supervisors and its author;
  anyone else gets the missing-card `404`, here and on `GET /api/memory/cards/company/{key}` (D103).
- **Lift:** one button per higher scope you may write (your teams and departments; `company` for supervisors), only on a
  published card. A refused or forged lift is the API's `403`.
- **Rename key:** shown only where you may write the card's scope; posts the version you saw.
- A successful lift or rename redirects to the card (a refresh re-sends nothing).
- Forms carry antiforgery tokens and the origin guard applies, like the other pages.
- Not built: MinIO/S3 pointers for large bodies (the `source` field is the pointer; the bank stores no blobs), display
  names for `who` (user ids are shown, "you" for yourself).

## Studio (agents as pull requests)

Todo slice 3 (`docs/design/studio.md`, D020–D024, D028–D033, D045, D109–D123). Builders compose agents; a pull request
in the agent repo `skanyxx-agents` carries them; only a supervisor's merge makes one live in kagent.

- **Repo (D023).** Setup creates org `skanyxx` and repo `skanyxx-agents` (private, `main`, protected: no direct writes,
  only Skanyxx's account merges) in the bundled git — locally Gitea — from the reconciler, which setup wakes (setup
  never waits on git); it retries while git is down. The repo's git id is then recorded (D119): it is **never
  re-created**, and a gone or different repo, a public one or an unprotected `main` stops the studio (Error, nothing
  changed) until it is restored or the owner confirms (`POST api/studio/confirm`). With no git configured the Studio
  page says which settings are missing. One folder per agent:
  `agents/<name>/agent.yaml` (a kagent `v1alpha2` Agent exactly as it is applied) and `agents/<name>/grants.yaml` (its
  Skanyxx memory grants) — always in the same pull request.
- **`/Studio`** (builders, supervisors, the owner; never employees): the form (name, description, model from kagent's
  ModelConfigs, instructions, skills as `@sha256:`-pinned OCI refs from `Studio:SkillRegistries` (empty = skills off,
  D122), tools of allow-listed MCP servers `Studio:McpServers`, search/upsert
  grants per `company`/`team:`/`department:` scope, optional kagent TTL memory). **Propose** = one branch, one commit in
  the builder's name, one PR; nothing touches kagent. Names start with a letter (≤ 33 characters). One open proposal per
  agent, at most `Studio:MaxOpenProposalsPerBuilder` (10) per builder; a name memory holds for someone else (an agent
  the owner made by hand) is refused (D117); changing an agent is a new PR
  ("Propose a change" on an agent in main). The memory MCP server is not picked: an agent with a grant gets its own
  `skanyxx-memory-<name>` with `memory_search` for a search grant and `memory_upsert` for an upsert grant.
- **Factory** (builders and the owner): describe the agent in words; the factory agent (`skanyxx-factory`, not merged,
  no tools) drafts the form. It opens no PR.
- **Review and merge** (supervisors and the owner): the page shows the YAML at the PR's head and any problem. The files
  are checked again at merge — exactly one agent folder, only those two files, exactly the shape the form renders
  (any other kind or field — BYO image, deployment, service account, labels, another namespace — is refused), allow-listed
  servers only, no name kagent already runs outside the studio (`seed`, ticket stages) — and the merge is pinned to that
  head commit (a later push is a `409`). A grant that opens or closes a **team or department** needs the **owner's**
  merge (D091). Builders get `403`.
- **Reconciler (D045).** On merge, and every `Studio:ReconcileSeconds` (60), it makes kagent match main through kagent's
  HTTP API (no kubectl, no Kubernetes credentials): grants into memory, a studio-issued memory secret (never acting for
  users, `created_by = studio`) handed to kagent as the Secret of the agent's own RemoteMCPServer
  (`Studio:MemoryMcpUrl` is `/mcp/memory` as pods reach it), and the Agent labelled `skanyxx.dev/merged: "true"` — so
  it appears in Chat. Unchanged agents are not written; an agent removed from main is removed, but never more than
  `Studio:MaxRemovalsPerPass` (2) or half of them in one pass, and none when main has no `agents/` (the owner confirms
  a bigger removal with `POST api/studio/confirm`); an invalid or hostile folder in main (anchors/aliases, deep nesting,
  over 64 KB) is logged at Error and skipped (the running agent stays). The memory secret's fingerprint names kagent's
  Secret, and memory records which one kagent holds, so a failure half way is repaired by the next pass (D118). One
  pass at a time across replicas (a Postgres advisory lock in memory's database, D120); a pass that fails is logged
  and the loop goes on.
- **Preview (D030, D032).** "Start preview" deploys `preview-<pr>-<name>`: not labelled merged (never in Chat), memory
  **search on `company` only, never upsert**, no TTL memory, no skills, never acting for users. Builders and supervisors chat with
  it on the proposal; it is removed when the PR merges or closes.
- **API:** `GET api/studio`, `GET api/studio/options`, `GET api/studio/agents/{name}/draft`, `POST api/studio/proposals`,
  `GET api/studio/proposals/{n}`, `POST api/studio/proposals/{n}/merge|close|preview|chat`, `POST api/studio/factory`
  (rate limited and origin guarded like `/api/chat`; so are the `/Studio` form posts). Owner only:
  `POST api/studio/agents/{name}/suspend|resume` — the emergency stop (D121: memory access revoked at once, out of
  kagent, kept out by every pass until resumed) — and `POST api/studio/confirm` (D119).
- **Config:** `Studio:Git:BaseUrl`, `Studio:Git:Token` (the one git account's token; never logged or returned),
  `Studio:Git:User` (`skanyxx-bot`), `Studio:Git:Owner`/`Repo`/`Branch`, `Studio:Namespace` (`kagent`),
  `Studio:MemoryMcpUrl`, `Studio:McpServers`, `Studio:ReconcileSeconds`, `Studio:FactoryModelConfig`,
  `Studio:SkillRegistries`, `Studio:MaxRemovalsPerPass`, `Studio:MaxOpenProposalsPerBuilder`, `Studio:LockWaitSeconds`.
- The legacy `/api/agents` writes (kubectl apply/scale/delete) are the owner's only (D109), and so is every other
  leftover route that changes kagent or the cluster or runs a process: tool servers, cloud tools, hooks, alerts,
  sessions (D116).

**Local Gitea.** `scripts/dev/first-hour.sh git` starts the compose service `gitea` (project `skanyxx`,
`127.0.0.1:3300`, registration off, sign-in required), creates the account `skanyxx-bot` (not a site admin; it owns the
org) with a random password nobody keeps, revokes its earlier `skanyxx-*` tokens and writes a new one scoped
`write:organization,write:repository` to `$GIT_TOKEN_FILE` (default `~/.config/skanyxx/gitea-token`, mode 0600); `host` then
sets `Studio:Git:*` and `Studio:MemoryMcpUrl`. Remove it with
`docker compose -p skanyxx rm -sf gitea && docker volume rm skanyxx_skanyxx-gitea` (and delete the token file).

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
  yet).
- Any signed-in user can read every run, its report and its dataset (which holds full prompts,
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
AX is pre-1.0 and rewrote its API in v0.3.0, so re-vendor deliberately. Shown on a **real** AX v0.3.1 on Agent Substrate (local kind cluster, arm64, gVisor) on 2026-10-08: list, run
(Pending → Running), watch (SSE `initial` / `modified` / `final`), get, stop and suspend all worked through
`/api/sandboxes` as the owner on the owner's own tasks; **resume failed** for both tasks Skanyxx started — see
"Local AX on kind" below.

```bash
dotnet test tests/Skanyxx.Module.Sandboxes.Tests   # no Docker needed; AX is faked
```

### Local AX on kind (dev only)

Recipe from the 2026-10-08 spike (task `2026-10-08_0830_skanyxx-ax-real-spike`, `evidence/spike.md` has every command):
Substrate's own kind quickstart pinned to the commit AX v0.3.1 builds against (`agent-substrate/substrate@672533541dbf`,
`KIND_CLUSTER_NAME=ate hack/create-kind-cluster.sh` + `hack/install-ate-kind.sh --deploy-ate-system
--credential-provider='{"name":"k8s.io"}'`), then `google/ax@v0.3.1` `make deploy AX_IMAGE_REPO=localhost:5001`
(with a kubeconfig that holds only `kind-ate`: the Makefile uses the current context). Three things AX's deploy does not
do, all needed:
- a **WorkerPool** (Substrate's sandbox demo, `--deploy-demo-sandbox`, labels `workload: sandbox`) and AX's base
  ActorTemplate **`ax-system/default-template`** selecting it (`kubectl-ate create actor-template`); without them every
  task fails with `no free workers available`. One task holds one worker.
- `AX_SNAPSHOTS_BUCKET` on `ax-controller` points at a developer's personal GCS bucket upstream; set it to the local
  store (`gs://ate-snapshots/ax/`).
- a task image: AX's example image is private. A local image of the v0.3.1 `ax-task-runner` (built from source for the
  host arch) on alpine runs plain `spec.command` with no Antigravity.

Then the Host with `Sandboxes__Enabled=true Sandboxes__Address=http://127.0.0.1:<port>` (a `kubectl port-forward
--address 127.0.0.1` to `ax-server`: AX has no auth, never bind it wider), `Sandboxes__AllowedImages__0=<that image>`
and `Sandboxes__NetworkIsolationConfirmed=true` only after the egress test in `deploy/sandboxes/README.md` passed.

**Resume failed** for both tasks Skanyxx started in the spike: AX v0.3.1 gives a task that names its own image a per-task ActorTemplate,
and resuming it asks Substrate for that template's golden snapshot, which does not exist (`FailedPrecondition … a
Golden data resume requires the ActorTemplate golden snapshot`). A task on the default template suspends and resumes
fine. Skanyxx always sends `image`, so suspend works and resume leaves the task `Failed` — an upstream limit, not
fixed here.

**Page:** `/Sandboxes` (owner and supervisor; in their nav, labelled Experimental) — run a task (name, an allowed image,
command), watch it start (live, the same SSE stream), list, suspend / resume / stop. The page holds no rule: its script
calls `/api/sandboxes` with the person's cookie, so the module's checks are the only ones, and an API refusal is shown as
its text. While the module is off the page says how to turn it on and calls nothing.

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
  is for the creator or a supervisor (role `owner` or `supervisor`; `403` otherwise). Supervisors cannot replace another
  user's task. Tasks created outside Skanyxx are supervisors' only.
- What may run: `image` must match an `AllowedImages` prefix (empty list = every run is `400`). A prefix matches
  only at a boundary: `/`, `@`, or a `:` that starts a tag (no `/` after it), so `ghcr.io` does not admit the
  registry port `ghcr.io:5000/…`. End prefixes in `/` (`ghcr.io/acme/`). `RequireDigest` also demands `@sha256:`. `requests`/`limits` above `MaxCpu`/`MaxMemory` (default `4` / `8Gi`) are `400`; missing
  ones get the defaults (requests `250m`/`256Mi`, limits `1`/`1Gi`).
- Caps (`429`): global `MaxActiveTasks` (50) and per-user `MaxActiveTasksPerUser` (3), counting tasks that are not
  Failed/Completed/Terminating (suspended count), via `ListTasks`. Re-running a stopped/failed task counts as a
  new one. Every run that makes a task active counts and writes under one atespace-wide lock, so parallel runs
  under different users cannot pass the global cap together; replacing an already-active task is not
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
  `AX_WORKSPACES_YAML` (or its metadata URL) and call `/mcp/memory` itself**. Since D080 that endpoint answers only
  an agent secret (`Authorization: Bearer`) and ignores `X-Agent-Id`, and Sandboxes issues none, so memory attach
  does not work end to end until the per-task credential below exists. Modules do not call each other, so
  Sandboxes never writes grants or secrets itself.

**Deployment prerequisites (security).** AX has no authentication (google/ax#376); these are not optional.
NetworkPolicy manifests for both rules below are in `deploy/sandboxes/` (`ax-server-ingress.yaml`,
`sandbox-egress-deny.yaml`, optional `sandbox-egress-internet.yaml`): `kubectl apply -k deploy/sandboxes/`
(verified on kind + gVisor, applied to the WorkerPool's namespace — D178; re-test on GKE / micro-VM, see its README).

- NetworkPolicy on `ax-system`: only Skanyxx's pods reach `ax-server:8080` (keep `ax-controller` ↔ Redis open).
- Default-deny **egress** for sandbox pods, then explicit allows: DNS, and internet only as `0.0.0.0/0` **except**
  the cluster pod/service CIDRs, RFC1918 (`10/8`, `172.16/12`, `192.168/16`) and link-local `169.254.0.0/16`.
  Must block: kube-apiserver, cloud metadata `169.254.169.254`, AX Redis/Dragonfly, the Substrate API,
  `atenet-router` (its metadata path serves other tasks' env) and every Skanyxx port. A Kubernetes NetworkPolicy
  does apply to gVisor sandboxes on kind when it targets the Substrate WorkerPool's namespace (D178); micro-VM and GKE
  are unverified — confirm on the target cluster before relying on it.
- **Sandboxes must not reach Skanyxx at all; `MemoryMcpUrl` stays empty (enforced at startup).** A separate memory port is **not
  implemented**: `/mcp/memory` is mapped on the main pipeline, so any extra Kestrel port/Service serves the whole
  API (`/api/sandboxes/*`, `/api/memory/*`, Tickets, kagent proxies) — and `/mcp/memory` believes whatever `X-User-Id`
  a sandbox sends, so it could write any user's memory. A NetworkPolicy filters ports,
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

- Memory attach is not safe yet. `/mcp/memory` now needs an agent secret (D080), but a secret handed to a sandbox is
  readable by whatever it runs, and the sandbox still chooses the `X-User-Id` it sends, so it could act as any user
  within that agent's grants. The image does not bound this — `command` is
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
- Caller identity is the signed-in user (cookie or bearer); supervisors are users with role `owner` or `supervisor`.
  Until invites exist the owner is the only user, so every task belongs to the owner.
