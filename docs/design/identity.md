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
