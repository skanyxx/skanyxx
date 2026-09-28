# Skanyxx

SRE Platform for Kubernetes management, agent orchestration, and monitoring. Built on a modular plugin architecture where each UI tab (Agents, Alerts, Chat, etc.) is an independently loadable DLL.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
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
| `Skanyxx:AllowedOrigins` | Browser origins allowed to send unsafe requests (POST/PUT/PATCH/DELETE) to **any** route on the Host, plus every request to `/api/memory`, `/api/tickets`, `/api/sandboxes`, `/api/identity` and `/mcp` (default `[]`). On routes outside those prefixes, the Host's own origin (Origin host:port equal to the Host header) is also allowed, so the app's own forms and fetches work without listing it. Any other `Origin` gets `403`; requests without `Origin` (non-browser clients) pass |
| `Skanyxx:RateLimit` | Per client address (IPv6 grouped per /64), on the guarded prefixes only (`/api/memory`, `/api/tickets`, `/api/sandboxes`, `/api/identity`, `/mcp`); legacy controllers, pages and static files are not limited: `PermitLimit` (200) per `WindowSeconds` (10) → `429`. Behind a proxy the partition is the proxy's IP (ForwardedHeaders not configured) |
| `Skanyxx:SignInRateLimit` | Stricter window, per client address (IPv6 per /64), over the credential posts only — sign-in, bootstrap, unlock, `/Login`, `/Setup`: `PermitLimit` (10) per `WindowSeconds` (60) → `429`. Sign-out and refresh are outside it |
| `Skanyxx:HealthCheckTimeoutSeconds` | Per-check timeout for `/health` (5). `/health` is anonymous and lists each check's name and status; no exception text, no CORS |
| `KAgent` | KAgent API connection (BaseUrl, Port, Protocol, Token) |
| `Kubernetes` | Optional kubeconfig path |
| `AWS` | AWS profile and region for cloud tools |
| `Azure` | Azure config directory |
| `Modules` | Plugin directory and enable/disable flags |
| `ConnectionStrings:Identity` | Postgres for accounts, roles and the Data Protection key ring (required). **Use a separate database role (or database) from the other modules** — whoever can read the key ring can mint sessions |
| `Identity` | `BootstrapToken` (`""`; needed outside Development to create or unlock the owner — empty does **not** stop startup, but outside Development `/Setup` and bootstrap answer `403`, unlock `404`, and there is no break-glass sign-in; a malformed token (under 32 characters, or with leading or trailing whitespace) stops startup; also guards `unlock` and the owner's break-glass sign-in — see Identity below), `PasswordMinLength` (12, at least 12), `LockoutMaxFailedAttempts` (5), `LockoutMinutes` (15), `SessionDays` (`7, 1–90`; absolute cap on a cookie session and on a refresh-token chain), `DataProtectionCertificatePath` / `DataProtectionCertificatePassword` (`""`; a certificate that encrypts the key ring at rest; a warning is logged outside Development when unset), `SecurityStampValidationSeconds` (60), `MaxPoolSize` (20). Validated at startup |
| `ConnectionStrings:Memory` | Postgres for the memory engine (required; startup fails without it) |
| `Memory` | `SearchTopK` (5), `UpsertsPerMinute` (30, per caller), `UpsertsPerMinuteTotal` (300, all callers), `MaxPoolSize` (40). Supervisors (write/lift into `company`, set agent grants) are users with role `owner` or `supervisor` |
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
| Chat | `/api/chat` |
| CloudTools | `/api/cloud`, `/api/cloudtools` |
| Dashboard | `/api/dashboard` |
| Debug | `/api/debug` |
| Hooks | `/api/hooks` |
| Identity | `/api/identity/status`, `/bootstrap`, `/sign-in`, `/refresh`, `/sign-out`, `/me`, `/unlock` |
| Investigate | `/api/investigate` |
| Memory | `/api/memory/cards`, `/api/memory/grants`, `/api/memory/agents/{agentId}/secret` (+ MCP at `/mcp/memory`, agent secret) |
| Tickets | `/api/tickets/issues`, `/api/tickets/pipelines`, `/api/tickets/runs` |
| Sandboxes (experimental) | `/api/sandboxes/tasks`, `/api/sandboxes/workspaces`, `/api/sandboxes/models` |
| Sessions | `/api/sessions` |
| Settings | `/api/settings` |
| ToolServers | `/api/toolservers` |

Health check: `GET /health` (anonymous; overall status plus each check's name and status, e.g.
`identity-postgres: Healthy`; no exception text)

## Identity and security model

Every page and API requires a signed-in user (fallback authorization policy). Local accounts live in Postgres
(ASP.NET Core Identity, `Skanyxx.Module.Identity`); there is no bundled IdP.

**First run — the owner (D025).** Until an account exists, `/Login` sends you to `/Setup`, which creates the
**owner** (role `owner`, also treated as supervisor until invites and roles land) and signs you in.
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
  installer does not generate one: set one yourself before first use.
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
  Secure outside Development, 8 h sliding. The security stamp is re-checked every `Identity:SecurityStampValidationSeconds` (60 s; 0 = every request), and the
  session ends `Identity:SessionDays` after sign-in no matter how active it is.
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
  ends **every** session of that user on every device: all refresh tokens at once, all browser cookies at their next
  stamp check (≤ 60 s by default). Access tokens already issued run out on their own (≤ 1 h, and never after the session cap). If the
  rotation cannot be saved, sign-out fails loudly (`500` ProblemDetails, never `204`) instead of reporting success — retry it.
- `GET /api/identity/me` → `{id, email, displayName, roles}`. The nav shows the signed-in user and a Sign out button.
- Rate limits: credential posts (sign-in, bootstrap, unlock, `/Login`, `/Setup`) have their own window
  (`Skanyxx:SignInRateLimit`); sign-out and refresh do not count against it, so a client can always sign out. Both
  windows are per client address, with IPv6 grouped per /64 (one host usually owns a whole /64).
- Unauthenticated API calls get `401` ProblemDetails (never a redirect); pages redirect to `/Login`. Anonymous:
  `/health`, static files, `/Login`, `/Setup`, `/Privacy`, `/Error`, `/Offline`, `GET /api/identity/status`, the
  bootstrap/sign-in/refresh/unlock endpoints. `/mcp/memory` needs no sign-in but an agent secret (below).
- CSRF: every POST/PUT/PATCH/DELETE on the Host (legacy controllers included) is refused with `403` when its `Origin`
  is not listed in `Skanyxx:AllowedOrigins` (the Host's own origin — Origin host:port equal to the Host header — is allowed implicitly off the guarded prefixes; `/api/{memory,tickets,sandboxes,identity}` and `/mcp` still need an allow-listed Origin for any browser caller); the cookie is SameSite=Lax; the Razor forms carry
  antiforgery tokens.

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
  with that certificate (e.g. mounted from a Kubernetes Secret). Without it the keys are stored unencrypted, and the
  Host logs a warning at startup outside Development.
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
- **Status:** `GET …/secret` → `{agentId, hasSecret, createdAt, actsForUsers}` (never the secret). **Revoke:** `DELETE …/secret` →
  `204` (`404` if there is none, `403` for a supervisor when the secret acts for users); the agent is locked out until a new secret is issued.
- **The user (`X-User-Id`, D084)** is honoured only when the agent's secret was issued with `actsForUsers: true`,
  and only when it is a user id (lowercase GUID). Otherwise it is ignored: the call has no user, so no personal
  scope — personal search finds nothing personal and personal upsert is refused. **Consequence, stated plainly: an
  agent that acts for users can read and write any user's personal memory just by naming that user's id**; the
  secret is the whole credential and Skanyxx cannot check that the user asked. That is why only the owner can turn
  it on. With it on, the header is exactly as trustworthy as kagent's own authentication — with kagent's Helm default
  `auth.mode: unsecure`, whoever can talk to kagent chooses it. **Run kagent with `auth.mode: secure`**, forward the
  header with `allowedHeaders: [x-user-id]`, and don't give such an agent shell or Kubernetes tools.
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
  must be listed in `Skanyxx:AllowedOrigins`; only the guarded prefixes (`/api/memory|tickets|sandboxes|identity`,
  `/mcp`) and the credential posts are rate limited — legacy controllers and pages are not; `/health` lists check names and
  statuses anonymously (no exception text).
- **Behind a proxy** both rate-limit windows see the proxy's address (ForwardedHeaders is not configured), so all
  clients share one sign-in window. Configure forwarded headers with known proxies when real ingress lands.
- **Passwords (D7):** at least 12 characters, no composition rules, no breached-password check (chosen during this
  slice; awaiting the user's confirmation).
- **Key ring storage (D6):** Data Protection keys in the identity database, unencrypted unless
  `Identity:DataProtectionCertificatePath` is set (chosen during this slice; awaiting the user's confirmation).
- Not verified against a real cluster or behind a real ingress yet.

**Next slices:** invites + role assignment (D026; `supervisor`, `builder`, `employee` roles already exist), teams and
departments (D055), the Entra ID mapper (group → role/team; unmapped users get no access, D027), a sandbox-facing
memory credential (per-task, so AX sandboxes can attach memory). Per-agent secrets for MCP (D080) are built (D083).
Design: `docs/design/identity.md`, D079–D084.

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
- kagent reaches the bank over MCP at `/mcp/memory` (`memory_search`, `memory_upsert`) with a per-agent secret: `POST /api/memory/agents/{agentId}/secret` as a supervisor, then give the agent's `RemoteMCPServer` an `Authorization: Bearer <secret>` header from a Kubernetes Secret (`deploy/kagent/memory/`). Agents without grants search `company` + the calling user's personal scope and may upsert only that personal scope (needs `X-User-Id`, honoured only for an agent whose secret the owner issued with `actsForUsers: true`); `PUT /api/memory/grants/{agentId}` replaces the default with explicit search/upsert per scope (at least one entry; revoke an agent with a single grant that has `canSearch` and `canUpsert` false).
- **Identity:** `/api/memory` acts as the signed-in user; on MCP the agent is the owner of the presented secret and the user is the `X-User-Id` the agent vouches for, if it may act for users (see Identity and security model).

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

On a machine with only the .NET 10 runtime, prefix `dotnet run` / `dotnet test` with `DOTNET_ROLL_FORWARD=Major`.
