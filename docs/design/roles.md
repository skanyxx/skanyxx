# Roles (in one UI)

D004 was owner / builder / employee. D024 adds **Agent supervisor**. Same product, four hats. One person may wear several.

| Role | Day-one job |
|---|---|
| **Owner** | First login, model credentials, invite users, optional Entra connect, group → role map |
| **Agent supervisor** | Merge factory/studio PRs; fleet is their responsibility |
| **Builder** | Propose agents (chat factory or studio); cannot merge |
| **Employee** | Chat with the seed (and later agents); memory cards, not YAML |

Git: setup **creates** `skanyxx-agents` (D023). Customer git if they hand us an org/token; otherwise git comes in the stack. Attach an existing repo later.

## Agents page (D250–D255, requirements.md §3.3)

The API decides; the page only hides what a role cannot use.

| Action (`api/agents`) | Owner | Supervisor | Builder | Employee |
|---|---|---|---|---|
| List | every agent | every agent | every agent | agents in Chat only |
| Details | full | full | full (read-only) | agents in Chat, no system prompt; others 404 |
| Form options | yes | yes | 403 | 403 |
| Create | yes | yes | 403 (propose in Studio) | 403 |
| Edit | yes¹ | yes¹ | 403 | 403 |
| Delete | yes¹ | yes¹ | 403 | 403 |
| Suspend / resume (D257) | yes¹ | yes¹ | 403 | 403 |

¹ Not studio agents (change them with a Studio proposal) and not system agents (seed, skanyxx-factory, skanyxx-*,
preview-*, ticket-flow stages): those answer 409 for everyone, the owner too (D252).

## Dashboard and Analytics (D318–D320, requirements.md §3.1, §3.11)

The API decides (`api/dashboard/*`, `api/analytics/*`); the nav only hides what a role cannot open.

| Action | Owner | Supervisor | Builder | Employee |
|---|---|---|---|---|
| Dashboard page, `GET api/dashboard/health` | yes | yes (read-only) | 403 / AccessDenied | 403 / AccessDenied |
| `GET api/dashboard/summary` | yes, with top users and recent admin events | yes, usage of the people in their own teams only, without per-person lists and audit (`null`) | 403 | 403 |
| Analytics: own usage (`scope=me`) | yes | yes | yes | yes |
| Analytics: a person | anyone | people in their own teams | 403 | 403 |
| Analytics: a team | any | teams they are a member of | 403 | 403 |
| Analytics: a department, the organization | yes | 403 | 403 | 403 |
| CSV export (`GET api/analytics/export`, audited `analytics.exported`) | yes | 403 | 403 | 403 |

A scope a caller may not open is 403 whether it exists or not (only the owner gets 404 for an unknown slug or id).
