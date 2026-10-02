# Leftovers (closed this pass)

Dragonfly is **on by default** (D068): AX’s Redis-protocol store, and later memory cache/locks. Not an agent bus. Cards stay in Postgres.

| Leftover | Call |
|---|---|
| Session/audit TTL | Transcripts **14 days** default, owner can change. Cards do **not** expire on that TTL. Who/when/version on the card is the write audit. |
| Merge → cluster | **Our reconciler** applies merged YAML to kagent. Customer Flux/Argo optional later. |
| Memory engine vs Joicy | **Our engine** (cards, collections, version, grants) exposed as MCP. Joicy stays a sister (dev/git). Do not wrap Joicy as the company bank. |
| Where setup runs | **Customer Kubernetes**, one installer/Helm: Skanyxx + kagent + memory + git. |
| Library vs agent grants | Day one: any logged-in user can browse **published `company`**. Agent search/upsert grants are separate. People-ACLs on extra collections come later. |

Still not this pass: **community catalog** (step 5) — recipes only (D005), imported into the customer install.
