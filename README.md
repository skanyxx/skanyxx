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
| `Skanyxx:AllowedOrigins` | Browser origins allowed to send unsafe requests (POST/PUT/PATCH/DELETE) to **any** route on the Host, plus every request to `/api/memory`, `/api/tickets`, `/api/sandboxes`, `/api/identity` and `/mcp` (default `[]`). On routes outside those prefixes, the Host's own origin (Origin host:port equal to the Host header) is also allowed, so the app's own forms and fetches work without listing it. Any other `Origin` gets `403`; requests without `Origin` (non-browser clients) pass |
| `Skanyxx:RateLimit` | Per client address (IPv6 grouped per /64), on the guarded prefixes only (`/api/memory`, `/api/tickets`, `/api/sandboxes`, `/api/identity`, `/mcp`); legacy controllers, pages and static files are not limited: `PermitLimit` (200) per `WindowSeconds` (10) → `429`. Behind a proxy the partition is the proxy's IP (ForwardedHeaders not configured) |
| `Skanyxx:SignInRateLimit` | Stricter window, per client address (IPv6 per /64), over the credential posts only — sign-in, bootstrap, unlock, `/Login`, `/Setup`: `PermitLimit` (10) per `WindowSeconds` (60) → `429`. Sign-out and refresh are outside it |
| `Skanyxx:InviteRateLimit` | Its own window, per client address (IPv6 per /64), over everything that checks an invite token — `/Invite` (GET and POST), `POST /api/identity/invites/lookup` and `/accept`: `PermitLimit` (10) per `WindowSeconds` (60) → `429`. Separate from the sign-in window, so link unfurlers and crawlers opening invite links cannot spend it |
| `Skanyxx:HealthCheckTimeoutSeconds` | Per-check timeout for `/health` (5). `/health` is anonymous and lists each check's name and status; no exception text, no CORS |
| `KAgent` | KAgent API connection (BaseUrl, Port, Protocol, Token) |
| `Kubernetes` | Optional kubeconfig path |
| `AWS` | AWS profile and region for cloud tools |
| `Azure` | Azure config directory |
| `Modules` | Plugin directory and enable/disable flags |
| `ConnectionStrings:Identity` | Postgres for accounts, roles and the Data Protection key ring (required). **Use a separate database role (or database) from the other modules** — whoever can read the key ring can mint sessions |
| `Identity` | `BootstrapToken` (`""`; needed outside Development to create or unlock the owner — empty does **not** stop startup, but outside Development `/Setup` and bootstrap answer `403`, unlock `404`, and there is no break-glass sign-in; a malformed token (under 32 characters, or with leading or trailing whitespace) stops startup; also guards `unlock` and the owner's break-glass sign-in — see Identity below), `PasswordMinLength` (12, at least 12), `LockoutMaxFailedAttempts` (5), `LockoutMinutes` (15), `SessionDays` (`7, 1–90`; absolute cap on a cookie session and on a refresh-token chain), `DataProtectionCertificatePath` / `DataProtectionCertificatePassword` (`""`; a certificate that encrypts the key ring at rest; a warning is logged outside Development when unset), `MaxPoolSize` (20), `InviteDays` (7, 1–30), `PublicBaseUrl` (where people reach Skanyxx, e.g. `https://skanyxx.example.com`; invite links are built on it. Optional for startup: **unset outside Development, the app starts and logs a warning, and creating an invite is refused (`409`) until it is set**; Development without it uses the request's scheme and host. When set it must be written exactly — no leading or trailing spaces, backslashes, query, fragment or user info — and absolute `https://`, except a loopback host such as the desktop installs' `http://localhost:5282`, whose links never leave the machine; anything else stops startup. The template ships `http://localhost:5282`, so a server install must change it: a loopback value while Skanyxx listens on a non-loopback address is warned about at startup, and the People page shows the host each new link points at). Validated at startup. (`SecurityStampValidationSeconds` is gone: cookies are checked on every request, and the key is ignored.) |
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
| Identity | `/api/identity/status`, `/bootstrap`, `/sign-in`, `/refresh`, `/sign-out`, `/me`, `/unlock`, `/invites`, `/people`, `/org/departments`, `/org/teams` |
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
  — its password check, `POST /Account?handler=LinkMicrosoft` only — and the Microsoft callback `/signin-oidc`) have their own window (`Skanyxx:SignInRateLimit`).
  Starting "Sign in with Microsoft" (`POST /Login?handler=Microsoft`) checks nothing and is not counted, so one Microsoft
  sign-in spends one permit, at its callback; sign-out and refresh do not count against it, so a client can always sign out. Invite
  lookup and accept and the `/Invite` page (GET too) have another (`Skanyxx:InviteRateLimit`), so crawlers opening
  invite links cannot spend the sign-in window. All windows are per client address, with IPv6 grouped per /64 (one
  host usually owns a whole /64); behind a proxy that is the proxy's address, so everyone shares one window.
- Unauthenticated API calls get `401` ProblemDetails (never a redirect); pages redirect to `/Login`. Anonymous:
  `/health`, static files, `/Login` (the Microsoft sign-in too), `/signin-oidc`, `/Setup`, `/Invite`, `/Privacy`, `/Error`, `/Offline`, `GET /api/identity/status`, the
  bootstrap/sign-in/refresh/unlock endpoints and `POST /api/identity/invites/{lookup,accept}`. `/mcp/memory` needs no sign-in but an agent secret (below).
- CSRF: every POST/PUT/PATCH/DELETE on the Host (legacy controllers included) is refused with `403` when its `Origin`
  is not listed in `Skanyxx:AllowedOrigins` — except the Microsoft sign-in callback `/signin-oidc`, which Entra posts from its own origin and the OIDC handler protects (below) — (the Host's own origin — Origin host:port equal to the Host header — is allowed implicitly off the guarded prefixes; `/api/{memory,tickets,sandboxes,identity}` and `/mcp` still need an allow-listed Origin for any browser caller); the cookie is SameSite=Lax; the Razor forms carry
  antiforgery tokens.

**People, invites and roles (D026 as built: D086, D087).** Only the owner administers people: the **People** page
(nav link shown to the owner only) or the API below; any other role gets `403`, anonymous `401`.

- **Invite:** `POST /api/identity/invites {email, roles}` → `201 {inviteId, link, expiresAt}` (`409` naming
  `Identity:PublicBaseUrl` outside Development while it is unset — nothing is written and no older invite is revoked). `roles` is one or more
  of `supervisor`, `builder`, `employee` (`owner` is never grantable: `400`), and the email must use only the
  characters an account name may (ASCII letters, digits, `-._@+`; `400` otherwise). The link —
  `<Identity:PublicBaseUrl>/Invite?token=skx_inv_…`, never built from the request outside Development
  (256 random bits) — is in this response and on the page that created it **only**: the database keeps its SHA-256,
  the list never shows it, and it is not logged. Send it to the person yourself (no email yet). It is single-use and
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
  someone again does not restore anything. Sandbox tasks and workspaces the person started are not stopped (open.md).
- **Audit:** invite created/revoked/accepted, role change and disable/enable are logged at Warning with the actor
  and target ids and the client address (the connection's, as for agent secrets: the proxy behind one), never a
  token or password. Refused invite accepts and lookups are logged at Warning too (the invite id when one was found,
  otherwise just "invalid"), and so is a signed-in person holding none of an owner-only route's roles (actor, method, route template —
  not the path, which the caller writes). A failed revocation after a role change or disable is logged at Error.
  Commands that carry a token or password print them as `***`.

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
- **Groups are read only when someone signs in with Microsoft.** Removing a person from a group in Entra takes effect
  at their next Microsoft sign-in; until then their current session lives on (up to `Identity:SessionDays`). Disable
  the account on People to cut someone off at once (open.md).
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
  clients share one sign-in window (the Microsoft callback `/signin-oidc` counts in it too). Configure forwarded headers with known proxies when real ingress lands.
- **Passwords (D7):** at least 12 characters, no composition rules, no breached-password check (chosen during this
  slice; awaiting the user's confirmation).
- **Key ring storage (D6):** Data Protection keys in the identity database, unencrypted unless
  `Identity:DataProtectionCertificatePath` is set (chosen during this slice; awaiting the user's confirmation).
- Not verified against a real cluster or behind a real ingress yet.

**Next slices:** re-checking Entra groups between sign-ins, a sandbox-facing memory credential (per-task, so AX
sandboxes can attach memory). Built: per-agent secrets for MCP
(D080 → D083), invites and roles (D086–D089), teams and departments (D090–D092), Microsoft Entra ID sign-in (D093–D095),
the .NET 10 upgrade (D097).
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
