# Identity (deep enough)

**Day one:** setup creates the **owner**. They jump in, configure the model, add people (D025).

**Org tree is Skanyxx’s** (teams, departments). Identity providers only **map into** it.

| Path | When |
|---|---|
| Invite + create teams/depts in Skanyxx | Always. No Azure, no Okta, still works. |
| **Entra ID** (OIDC + group → role **and** group → team/dept) | Optional. |
| Other IdPs (Google, Okta, …) | Same mapper pattern later. |

Connecting an IdP is **not** the installer. Owner does it when ready. Break-glass owner always works.

**Group map (D027):** IdP group → Skanyxx role. Unmapped users **do not** get in. Optionally group → team/dept membership. The team/dept **objects** still exist in Skanyxx even if nobody uses Azure.

## As built (slice 1, 2026-09-26)

Owner bootstrap + sign-in only (D079, D081). ASP.NET Core Identity in the API, cookie + bearer, one `owner` account
(also supervisor until roles land). Break-glass = the bootstrap token (`POST /api/identity/unlock`). Agents on the
memory MCP authenticate with a per-agent secret (D080, built as D083). Details, session model and deploy requirements: README
"Identity and security model". Next slices: `open.md`.

## As built (slice 2, 2026-09-29) — invites and roles

Invite + role assignment (D026 as built: D086). The owner's **People** page (nav link visible to the owner only) and
`api/identity/people` / `api/identity/invites` list people, change roles (`supervisor`, `builder`, `employee`; never
`owner`), disable/enable, create invites and revoke pending ones. An invite is a single-use link shown once
(`/Invite?token=…`, hashed at rest, 7 days); the invitee sets a password and is signed in with the invite's roles.
Role changes and disabling end the person's sessions at once: refresh tokens, bearer access tokens (D087) and cookies
(D089) — both carriers check the security stamp on every request. The owner stays owner, stays
supervisor (D024), cannot be disabled, and the owner's roles cannot be changed (`403`). Details: README "Identity and
security model". Next slices: `open.md`.

QA round 1 hardening (D088): invite links are built on `Identity:PublicBaseUrl` (required and https outside
Development, loopback excepted); losing `supervisor` or being disabled revokes the memory agent secrets the person
issued (`PrivilegesRevoked`, handled by the memory module); only a pending invite takes the account lock; invite
lookup/accept/page have their own rate-limit window (`Skanyxx:InviteRateLimit`); a password must not contain the
email's local part; display names refuse invisible formatting characters; invite emails are checked against the
account-name characters at invite time; refusals (bad invite tokens, non-owners on owner routes) are logged with the
client address; secret-carrying commands print `***`.

QA round 2 (D089): cookies are stamp-checked on every request, closing the window in which a demoted supervisor's
cookie could mint an agent secret after the revocation ran; `PrivilegesRevoked` is published whenever roles are saved
without supervisor (unchanged too, so re-saving is the retry) and on every disable, not cancellable, logged at Error and
rethrown on failure; `GET …/agents/{id}/secret` shows `createdBy`. `Identity:PublicBaseUrl` no longer stops startup
when unset (upgraded installs have none): a warning at startup, and invites are refused (`409`) until it is set; a set
value is taken exactly as written and the link is built from the parsed URL; a loopback value on a non-loopback
listener is warned about. Owner-route refusals log the route template, and only for someone holding none of its roles.
