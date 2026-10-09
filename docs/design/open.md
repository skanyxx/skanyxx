# Open

AX Tasks pack locked (D063–D066, `ax-tasks.md`). Community paused.
AX v0.3.1 does not match D064/D067/D068 as written: see `ax-integration.md` (D069–D077, locked 2026-10-08, + open questions).

## Still open on memory UX

- Promote UI: button on a **card** vs “share cards from this **chat**” (can be both).
- Seed search: personal + company, or personal only until first lift?

## Platform

- ~~**.NET 8 reaches end of support on 10 Nov 2026**~~ — **done (D097):** every project targets `net10.0`;
  ASP.NET Core / EF Core / Npgsql packages are on their 10.x lines; no `DOTNET_ROLL_FORWARD` needed. Left over:
  ~~`Polly.Extensions.Http`~~ (removed, D170) and `xunit` (v2) are marked legacy on NuGet (successor `xunit.v3`); not
  vulnerable, not needed for net10, a separate change.
  The first-start `Failed executing DbCommand` Error is fixed: the migrators create the history table first (D098).
- ~~**Polly v7 -> `Microsoft.Extensions.Http.Resilience`**~~ — **done (D170, 2026-10-07):** removed instead of
  migrated: `AddResilientHttpClient` had no caller, so it went with every Polly package; no client retries.
  `xunit` v3 is still open (todo §6).
- ~~**Root `SkanyxxWeb.csproj`**~~ — **deleted with every root file only it used (D171, 2026-10-07).** The committed
  `skanyxx.db` (no credentials; one developer chat session) is still in git history.
- ~~**Root `bin/` and `obj/` are tracked in git**~~ — **untracked (D172, 2026-10-07).**
- ~~**The release workflow has no test step**~~ — **done (D173, 2026-10-07):** a `test` job runs the five suites on
  Ubuntu and every installer job needs it.
- ~~**Host tests on Windows lock `skanyxx.db`**~~ — **harness fixed (D174, 2026-10-07):** the SQLite pool is cleared
  before a host's temp content root is deleted. Not yet confirmed on a Windows machine.

## Studio (slice 3) — left over from QA round 2

- **Pre-D117 backfill gap (dev databases only, D117):** a studio agent that had grants but no secret before the
  ownership migration is not claimed and stays "taken" (a Warning each pass) until the owner clears its grants. Optional
  fix: also claim names whose kagent agent carries the studio's managed label.
- **The owner's view of sessions (D124):** kagent lists sessions per user, so the owner reads one person's at a time
  (`?user=`); an all-users list would need kagent's database or a Skanyxx-side session index.
- **Lone preview memory servers whose claim memory already gave back** (a delete kagent answered 404 before it had
  reconciled the server) are not swept (D123): harmless (the secret is revoked), cleaned up by hand.

## Next identity slices (after slice 1, D079–D081)

1. ~~Invites + role assignment~~ (D026) — **built** (D086, D087). Left over from it:
   - ~~**Invite delivery by email** (A1)~~ — **built (D150, D151):** with `Identity:Smtp` the link is emailed and not
     returned; without it the owner copies it as before.
   - ~~**Durable audit table**~~ — **built (D152):** `identity_audit`, written with each change, owner-only Audit page.
   - **Proxy access logs** may record the invite link's query string; single-use + 7 days bounds it (D086).
   - ~~Owner changing their own hats~~ — the owner's roles are fixed now (`403`, D088).
   - **Upgraded installs have no `Identity:PublicBaseUrl`** (D089): the installers never edit an existing
     `appsettings.json`. The app starts and logs a warning; creating an invite answers `409` naming the setting until
     it is set (`http://localhost:5282` on a desktop install). The installers say so on upgrade.
   - **Revocation of agent secrets is not transactional with the role change** (D088, D089): it runs after the
     commit; a failure answers `500` with the change saved and an Error log, and saving the same roles (or disabling)
     again re-runs it (`GET …/secret` shows `createdBy` for doing it by hand). A secret-issuing request that had
     already passed authentication when the demotion committed can still land after the revocation and survive;
     every later request, cookie or bearer, is refused, because both check the security stamp on every request.
     An outbox would close that instant.
   - ~~**Offboarding does not stop sandbox tasks**~~ — **built (D154):** a disable stops the person's active AX tasks.
     Still: **workspaces stay** (storage, owned by the person), and a demotion stops nothing (their own tasks stay theirs).
   - **A mint already in flight can outlive the sweep:** a secret-issue request that passed authentication just before a
     demotion or disable committed can insert after the revocation's DELETE (a window of milliseconds inside one request).
     Saving the person's roles again, or disabling again, publishes `PrivilegesRevoked` and sweeps it.
   - **No breached-password list** (accepted, D088): length ≥ 12 plus "not the email's local part". A k-anonymity
     HIBP check would be the next step.
   - ~~Disable racing a sign-in~~: the cookie from that sign-in carries the old stamp and its first request is refused (D089).
   - ~~**Invite rows are never purged** and `AcceptedUserId` has no FK~~ — **built (D155):** retention job and FK.
   - **Several owners / owner transfer**: one owner, never grantable; transfer would be its own slice.
   - ~~**Password reset**~~ — **built (D156, D157):** by email (with SMTP) or an owner-issued link. **Account delete,
     self-service profile**: still not built. A disabled account keeps its email, so it cannot be re-invited; the owner
     enables it instead.
2. ~~**Teams and departments**~~ (D055) — **built** (D090). Left over from it:
   - **No delete or archive** of departments/teams, and no leaving a team empty on disable: a disabled person stays a
     member (harmless — they cannot sign in), is listed with a *disabled* mark on the Org page, and **enabling them
     restores every membership without review** (accepted, SEC L3; remove them first if that is not wanted). Slugs
     are never reused because nothing is deleted; a delete would need a tombstone to keep that promise.
   - **Accepted (SEC L1, D091): pre-existing team/department cards** whose slug has no org object are readable only
     by owner/supervisor until the owner creates the matching team or department (same slug); then its members see
     them, including anything planted there before this slice (when any person could write any team scope). Only dev
     data exists before this slice, so no review step was built; a production install upgrading from it should list
     `team:`/`department:` scopes in `memory_cards` before creating teams. **Grants and secrets are not re-checked
     either (SEC N2):** a team/department grant a supervisor set before D091, and a secret a supervisor issued for such
     an agent, keep working after the upgrade. The owner should review every agent's grants on upgrade and rotate
     the secret of any team-granted agent whose secret someone else issued (only dev data exists today).
   - **Agent grants to `team:`/`department:` scopes are the owner's (D091)** — setting, replacing and issuing a secret
     for such an agent — but are **not checked against the tree**: the owner can grant a slug that does not exist yet
     (it matches nothing until it does). A grant exposes that team's cards to everyone who talks to the agent, and is
     not intersected with an acts-for-users agent's user's membership. ~~A secret a supervisor issued *before* the owner
     granted a team keeps working: the owner should rotate it when granting~~ — **enforced (D092):** the owner's team
     grant is refused (`409`) until the owner rotates a secret someone else issued.
   - **Supervisor oversight is audited, not refused (D091):** a supervisor may still lift a team card into company;
     every such lift by a non-member is a Warning line, as is every grant change and refusal. Reads and searches of
     team scopes by supervisors are not logged.
   - **Supervisors search every team/department** (oversight): their top-5 now mixes those cards with company ones.
   - ~~**No durable audit table**~~ — **built (D152):** org changes write audit rows too.
   - ~~**Entra group → team/department** mapping~~ — built with the Entra slice (item 3; teams only — a department
     still comes through its teams).
3. ~~**Entra ID mapper**~~ (D027, D079) — **built** (D093, hardened in D094 and D095). Left over from it:
   - ~~**Groups are read only at a Microsoft sign-in.**~~ **Built (D158):** a Graph re-check every
     `Identity:EntraRecheckMinutes` (60) re-maps or refuses managed accounts between sign-ins, so a removal reaches a
     signed-in person within the interval. What follows is the history before it. Someone removed from a mapped group in Entra keeps their current
     Skanyxx session (cookie: sliding 8 h, capped at `Identity:SessionDays`) until they next sign in with Microsoft. A
     linked account no longer escapes through its password while Microsoft sign-in is on (D094: password sign-in and
     refresh refused; D095: keyed on the owner's switch, and turning it on ends the managed accounts' sessions, so a
     password session opened while it was off does not survive either). Next step: re-check with Graph `checkMemberGroups` from the stamp validator (every request
     already reads the user) with a per-user cache, plus a nightly sweep that applies D2 to accounts Entra no longer
     maps. Until then, the owner disables the account.
   - **Turning Microsoft sign-in off (or moving tenant) ends nothing**: existing sessions of Entra-managed accounts run
     out on their own, and accounts created by Entra have no password, so they cannot sign in again until it is back on
     — or until the owner issues them a password-reset link, which works while Microsoft sign-in is off (D157); their
     email cannot be re-invited. The stored secret stays when sign-in is off; there is
     no "remove secret". The owner can remove a person's Microsoft login on People (D094) only from an account that
     also has a password (D095): for an account Entra created it is the only sign-in, removal could not be undone, and
     the answer is `409` "…disable it instead" (Disable can be reversed; People shows no button for those accounts).
   - **Guests need the optional `acct` claim**: without it nobody gets in (D094, fail closed, Error log naming the
     claim); an `idp` naming another tenant is a guest whatever `acct` says. The settings page does not yet show "acct
     missing" after a refused sign-in — the owner learns it from the person or the log.
   - **Email as account name** (CR L5, not changed): a member whose `mail` is set to someone else's address can create
     the account under it first (squatting, not takeover: the key is `tid|oid`). `xms_edov = false` now refuses
     creation (D094), but only when the tenant emits that optional claim; UPN-first naming or a verified-domain check
     remain options.
   - **No-password accounts cannot link**: linking re-checks the password (D094), and a link from an account without
     one is refused before the check (D095). Since D095 such an account never loses its Microsoft login in the app;
     a password reset (D157) is possible only while Microsoft sign-in is off.
   - **Graph permission to verify on a real tenant:** `GroupMember.Read.All` (application, admin consent) is what the
     contract names for `/users/{oid}/checkMemberGroups`; Microsoft's permission table may also want
     `User.ReadBasic.All`. Tested only against a fake Graph.
   - **Not verified against a real Entra tenant.** The flow is tested against navikt/mock-oauth2-server (form_post,
     PKCE, per-tenant issuers). The mock stamps `tid` from the issuer and does not bind codes to issuers, so "a token
     from a foreign issuer" is not staged (the handler's issuer validation is framework code).
   - **Client secret only**: no certificate or federated credential; Entra secrets expire (≤ 24 months) — rotate on
     the page before that. Global cloud only (no sovereign clouds), single tenant only, group ids only (no app roles).
   - **Email is taken once**: the account's email/user name come from the first sign-in and are not updated when they
     change in Entra (they are display data; the key is `tid|oid`).
   - **A failed `PrivilegesRevoked` at sign-in** (memory database down) answers `500` after the change is saved (no
     session), like a People save. Since D095 only a sign-in that owes a revocation publishes (supervisor taken away
     now, or an earlier failure still marked in `identity_pending_revocations`), so everyone else signs in with
     Microsoft while the memory database is down; the owed one is retried by the account's next Microsoft sign-in or
     People save. A **password** sign-in does not retry it: an owed revocation of an account that never signs in with
     Microsoft again waits for the owner's next save of that account (the Error log says so). A save or Microsoft
     sign-in that leaves the account with supervisor drops the owed revocation unpublished (D096).
   - **Rate limit behind a proxy**: one Microsoft sign-in now spends one permit (its callback, D094), but all clients
     still share the proxy's window until forwarded headers with known proxies are configured.
   - **Other replicas** pick up a settings change within 30 s (version poll); the saving replica at once. Saves are
     serialized and each gets its own version (D094). If the saving replica's reload fails, the save still answers
     `200` with "apply within a minute". D8's on/off check reads the settings row itself (D096), so no replica issues a
     managed account a password session after the switch-on has committed.
   - **Changing the tenant or the group map while sign-in stays on ends no sessions** (D095 ends them only on
     off → on): a signed-in person keeps the roles and teams of the old mapping until their next Microsoft sign-in,
     bounded by the sliding cookie window and `SessionDays`. To cut someone off at once, disable the account.
   - **Client secret at rest**: outside Development without a Data Protection certificate the key ring is stored in
     clear next to the protected secret; warned about on the page and in the log (D094), not refused.
   - ~~**No durable audit table**~~ — **built (D152):** each of those Warning lines also writes an audit row.
   - **The password-sign-in answer for managed accounts is the wrong-password `401`** (D095, no oracle). Someone who
     forgot their account is managed sees "Invalid email or password."; the Login page's fixed hint points at the
     Microsoft button. Every password attempt on a managed account counts toward the lockout, right or wrong (D096:
     same database work, so no timing difference); a right one is also a Warning. The lockout does not block
     Microsoft sign-in, only the password, which starts no session while sign-in is on anyway.
   - **A secret that no longer decrypts** (lost Data Protection key) hides the Microsoft button and its hint while D8
     keeps managed accounts off their passwords (D095), so those people see only "Invalid email or password."; the
     owner sees the Error log and the settings page. Re-entering the secret fixes it.
   - **Turning sign-in on rotates stamps with one `UPDATE`** (D095), not under each account's lock: a concurrent
     password check on such an account can fail on the concurrency stamp change (its failed-count write is lost, or the
     attempt errors); the person just tries again. Accepted: the switch is rare and owner-only.
4. ~~Per-agent secret for the memory MCP~~ (D080) — **built** (D083): supervisors issue/rotate/revoke, `/mcp/memory`
   answers only `Authorization: Bearer <agent secret>`, `X-Agent-Id` is gone. Left over from it:
   - **Sandbox memory credential:** a per-task credential (and sandbox-facing listener) so AX tasks can attach memory
     (README "AX Tasks"); today attach is refused at startup and would need a secret no sandbox should hold.
   - **End user over MCP:** `X-User-Id` is vouched for by the agent and only as good as kagent's `auth.mode`; since
     D084 it is honoured only for agents the owner lets act for users, and only as a user GUID. A signed user
     assertion (kagent STS / token exchange) would remove the remaining trust.
   - **Secret UI:** issuing, rotating and revoking is API-only (no page yet). Issue/rotate/revoke are logged at
     Warning (D084); the durable audit (D152) is identity's, so memory's secret and grant events are still log lines only.
   - **Accepted risk (SEC L5, QA round 1): secrets never expire and record no last use.** A leaked secret works until
     someone rotates or revokes it, and nothing shows a secret being unused or used from an unexpected place. Later:
     a throttled `last_used_at` in `GET …/secret`, optionally a maximum age. Mitigated for now by the network
     requirements (no public `/mcp`, kagent-only NetworkPolicy, encrypted in-cluster traffic) and rotation.

## Identity — left over from section 6 (D150–D169, 2026-10-07)

- **An owner who forgot their password has no in-app path.** Break-glass (the bootstrap token) gets past a lockout but
  still checks the password, and password reset refuses the owner (D157). Recovery is a database-level runbook; a
  bootstrap-token-guarded owner reset would be its own decision.
- **The self-service reset queue is in-process** (bounded, 256): a restart loses requests waiting in it, and a full queue
  drops new ones (Warning). The person asks again; the answer was the same either way.
- **Emails are not retried.** Owner flows fall back to showing the link; a failed self-service email is a Warning and
  the person asks again after two minutes.
- **Not verified against a real mail provider:** tested with a fake sender and end to end against a local mailpit
  (task evidence). Office 365 / Gmail relays need STARTTLS on 587 and an app password or a relay connector.
- **Graph re-check limits (D158):** guests are not re-checked (that needs a token); keys of another tenant are skipped
  (counted in a Warning) — moving tenant still ends nothing; a per-request check from the stamp validator is not built;
  the interval (default 60 min) is the window a removed person keeps their session.
- **Audit details hold invited email addresses** (D152): PII bounded by retention (365 days by default). Memory's
  events (grants, secrets, oversight lifts) are still Warning lines, not audit rows.
- **A disable with sandboxes on and AX down answers `500`** (D154) after saving — the API's `detail` and the People page
  say so and that disabling again retries (D162); the person's tasks may still run until AX is back and the owner
  disables again. Workspaces are never stopped. An Entra re-check refusal stops tasks the same way and retries on the
  next sweep (D161); a refused Microsoft *sign-in* does not, the next re-check does.
- **Audit sampling is per replica and in memory (D167):** N replicas write up to N rows a minute per refusal kind, and a
  restart loses an open `skippedBefore` count (at most a minute's worth). The Warning lines carry every event.
- **One-time tokens in reverse-proxy access logs:** invite and reset links carry the token in the query string; a proxy
  or ingress access log records it unless its format drops the query (README deployment note). Stripping the token on
  landing (`history.replaceState`) is not done.
- **The audit is append-only against updates and TRUNCATE (D169), not deletes**: whoever holds the identity database
  role can delete rows (the retention job needs `DELETE`) or, as table owner, disable the triggers. A separate role for the purge, or shipping rows out (SIEM), would close that.
