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

## As built (slice 3, 2026-09-30) — teams and departments (D090)

The org tree lives in the identity module: **departments** contain **teams**, people are members of teams (0..n), and
department membership comes through one's teams. Only the owner manages it — the **Org** page (nav link for the owner)
or `api/identity/org/departments`, `api/identity/org/teams` and `…/teams/{slug}/members/{userId}`; the **People** page
and `GET api/identity/people/{id}/teams` show each person's teams. Slugs are memory scope ids (`team:<slug>`,
`department:<slug>`): lowercase, unique per kind, fixed for good (renaming changes the display name; a team can move to
another department) and never reused (nothing is deleted yet). Only enabled accounts can be added. Every change is
logged at Warning with actor, target and client address; non-owners on these routes are logged as owner-route refusals.

Memory reads membership through Core's `IOrgMembership` once per request (cached on the request only, nothing in
tokens), so adding, removing or moving counts from the next request. Members read, search, write and lift within their
teams and departments; owner and supervisors read every team and department scope but write only where they are
members; agents reach teams only by explicit grants, which only the owner sets (D091). Memory declares the identity
module as a dependency: the Host refuses to start with memory enabled and identity disabled.

QA round 1 (D091): adding a member takes the same account lock as disable/enable and re-reads the account under it,
so a concurrent disable cannot leave a disabled person added. Disabling keeps memberships (marked *disabled* on the
Org page); enabling restores them. Org and People page failures answer with the outcome's status (400/404/409).
Details: README "Identity and security model" → "Teams and departments".

## As built (slice 4, 2026-09-30) — Microsoft Entra ID sign-in (D093)

Entra ID is an optional **external login** onto Skanyxx's own accounts (D027, D079): "Sign in with Microsoft" runs
OIDC code flow + PKCE against the owner's single tenant, then Skanyxx issues its own cookie. The owner turns it on
and maps **group object ids → roles and teams** on the **Microsoft sign-in** page (`api/identity/entra/settings`);
the settings live in the identity database, the client secret Data-Protection-protected and write-only, and a save
applies to the next sign-in without a restart. People in no mapped group, and guests, do not get in. The account is
keyed on `tid|oid`, never on email: a first sign-in creates the account unless its email already has one (then the
person signs in with the password and links Microsoft from their **Account** page, bound to their own user id).
An account with a Microsoft login is **managed by Entra**: at every Microsoft sign-in its roles and teams become
exactly what its groups map to (People and Org mark it); a refused sign-in empties them, ends its sessions and
revokes what it issued as supervisor. The owner is never changed by the mapping. Group overage is resolved with Graph
`checkMemberGroups` (app-only, mapped ids only). Setup (app registration, redirect URI, groups claim, Graph
permission, proxies): README "Identity and security model" → "Microsoft Entra ID sign-in". Leftovers: `open.md`.

**QA round 1 (D094).** While Microsoft sign-in is on, a managed account other than the owner signs in with Microsoft
only — its password sign-in and refresh tokens are refused, and work again once it is off — so a removal from the
mapped groups reaches it at its next session. A token without `acct` is refused (fail closed), and an `idp` naming
another tenant is a guest. Linking needs the current password (lockout rules apply) and ends the account's other
sessions; the owner can remove a Microsoft login on People (audited, sessions end, local-only). The owner cannot link
and is never reached through Microsoft. `PrivilegesRevoked` publishes at every sign-in, link or refusal whose mapping
lacks `supervisor`, so the next sign-in retries a failed one. Settings saves are serialized (advisory lock) with the
version bumped in SQL; a create and a link of the same `tid|oid` are serialized on that key.

**QA round 2 (D095).** `PrivilegesRevoked` is published only when one is **owed**: a sign-in, link, refusal, People
role save that takes `supervisor` away, or a disable, marks it in its own transaction (`identity_pending_revocations`,
one row per account with a generation id), and the mark is cleared only after a successful publish — so a failed one
is retried by the account's next save or Microsoft sign-in, and a sign-in that takes nothing away never touches the
memory database (replaces D094's publish-on-every-sign-in). The password rule follows the owner's **Enabled** switch,
not whether the secret still decrypts; a managed account's password gets the wrong-password `401` (no oracle, no
counter reset) and the Login page points everyone at the Microsoft button; turning Microsoft sign-in on ends every
managed non-owner account's sessions. A Microsoft login that is an account's only sign-in (no password) cannot be
removed (`409`; disable instead). Guest detection compares `idp` with the tenant's exact v1/v2 issuer. A sign-in queued
behind a login removal changes nothing; the link's password step-up never waits on the account lock (`429` busy).
