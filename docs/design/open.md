# Open

AX Tasks pack locked (D063–D066, `ax-tasks.md`). Community paused.
AX v0.3.1 does not match D064/D067/D068 as written: see `ax-integration.md` (proposed D069–D077 + open questions).

## Still open on memory UX

- Promote UI: button on a **card** vs “share cards from this **chat**” (can be both).
- Seed search: personal + company, or personal only until first lift?

## Next identity slices (after slice 1, D079–D081)

1. **Invites + role assignment** (D026): owner invites by email; `supervisor`, `builder`, `employee` roles already seeded.
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
