# Open

AX Tasks pack locked (D063–D066, `ax-tasks.md`). Community paused.
AX v0.3.1 does not match D064/D067/D068 as written: see `ax-integration.md` (proposed D069–D077 + open questions).

## Still open on memory UX

- Promote UI: button on a **card** vs “share cards from this **chat**” (can be both).
- Seed search: personal + company, or personal only until first lift?

## Next identity slices (after slice 1, D079–D081)

1. ~~Invites + role assignment~~ (D026) — **built** (D086, D087). Left over from it:
   - **Invite delivery by email** (A1): today the owner copies the link and sends it; an SMTP/Graph sender later.
   - **Durable audit table**: invite/accept/revoke/role/disable are Warning log lines with actor and target ids only.
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
   - **Offboarding does not stop sandbox tasks or workspaces** a supervisor started: they stay owned by that person
     (`Ownership.CanManage`), harmless while they are disabled, and the person's own after a demotion. Stopping or
     reassigning them on `PrivilegesRevoked` is a follow-up.
   - **A mint already in flight can outlive the sweep:** a secret-issue request that passed authentication just before a
     demotion or disable committed can insert after the revocation's DELETE (a window of milliseconds inside one request).
     Saving the person's roles again, or disabling again, publishes `PrivilegesRevoked` and sweeps it.
   - **No breached-password list** (accepted, D088): length ≥ 12 plus "not the email's local part". A k-anonymity
     HIBP check would be the next step.
   - ~~Disable racing a sign-in~~: the cookie from that sign-in carries the old stamp and its first request is refused (D089).
   - **Invite rows are never purged** and `AcceptedUserId` has no FK; a retention job belongs with the durable audit.
   - **Several owners / owner transfer**: one owner, never grantable; transfer would be its own slice.
   - **Password reset, account delete, self-service profile**: not built. A disabled account keeps its email, so it
     cannot be re-invited; the owner enables it instead.
2. **Teams and departments** (D055): org-tree objects in Skanyxx; memory team/department scopes key on them.
3. **Entra ID mapper** (D027, D079): external login mapped onto local accounts; group → role/team; unmapped users get no access.
4. ~~Per-agent secret for the memory MCP~~ (D080) — **built** (D083): supervisors issue/rotate/revoke, `/mcp/memory`
   answers only `Authorization: Bearer <agent secret>`, `X-Agent-Id` is gone. Left over from it:
   - **Sandbox memory credential:** a per-task credential (and sandbox-facing listener) so AX tasks can attach memory
     (README "AX Tasks"); today attach is refused at startup and would need a secret no sandbox should hold.
   - **End user over MCP:** `X-User-Id` is vouched for by the agent and only as good as kagent's `auth.mode`; since
     D084 it is honoured only for agents the owner lets act for users, and only as a user GUID. A signed user
     assertion (kagent STS / token exchange) would remove the remaining trust.
   - **Secret UI:** issuing, rotating and revoking is API-only (no page yet). Issue/rotate/revoke are logged at
     Warning (D084); there is no durable audit table.
   - **Accepted risk (SEC L5, QA round 1): secrets never expire and record no last use.** A leaked secret works until
     someone rotates or revokes it, and nothing shows a secret being unused or used from an unexpected place. Later:
     a throttled `last_used_at` in `GET …/secret`, optionally a maximum age. Mitigated for now by the network
     requirements (no public `/mcp`, kagent-only NetworkPolicy, encrypted in-cluster traffic) and rotation.
