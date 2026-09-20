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
