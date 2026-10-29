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

## Sandboxes, ticket runs, studio previews (D392, requirements.md §2)

"Nobody who is not a supervisor can do harm." The API decides; `RoleMatrixTests` calls every write route, and every API
read (D394), as an employee and a builder and lists, line by line, the few they may still reach (their own chat,
preferences, quota and analytics; memory cards by scope; the agents catalog; proposals; the builders' Studio, Plugins
and Investigate routes). A route with an id outside those lists must be refused by its roles (401/403) before the
handler runs: a made-up id is a 404 from any handler and proves nothing.

| Action | Owner | Supervisor | Builder | Employee |
|---|---|---|---|---|
| Sandboxes page and every `api/sandboxes` route | yes | yes | 403 | 403 |
| Start a ticket run (`POST api/tickets/runs`) | yes | yes | 403 | 403 |
| Decide a gate (`POST …/runs/{id}/decision`) | any run | any run | 403 | 403 |
| Cancel a run | any run | any run | 403 | 403 |
| Jira reads (`GET api/tickets/issues*`) | yes | yes | 403 | 403 |
| Run reads: list, detail, report, dataset (`GET api/tickets/runs*`, D394) | yes | yes | 403 | 403 |
| A ticket's runs in Chat's ticket view (`GET api/chat/tickets/{key}`, D394) | yes | yes | left out | left out |
| Read, write or delete a pipeline (`api/tickets/pipelines*`; reads D394) | yes | yes | 403 | 403 |
| Agent memory secrets and grants (`api/memory/agents/{id}/secret`, `api/memory/grants/{id}`) | yes | yes¹ | 403 | 403 |
| Studio preview | no MCP tools | no MCP tools | no MCP tools | — |
| Chat with a preview that still has MCP tools (deployed before D392) | 409, removed by the reconciler (D396) | 409 | 409 | — |

¹ The handler keeps the owner-only cases (acting for users, team or department grants).

Losing supervisor, or being disabled, stops the person's active sandboxes and cancels their unfinished ticket runs.
Being disabled, or left below builder, cancels their Investigate runs (D395). Work picked up later asks identity again
(`IPersonStanding`): each ticket stage, each Investigate run and step, and a sandbox start, so a request let in just
before the role was saved away still stops. A sandbox start asks both before AX creates it and after, and deletes the
sandbox if the role went meanwhile (D398). An open sandbox watch is not cut; it ends within `Sandboxes:WatchSeconds`.

Memory's credential check on cards (D391) catches accidents and is not a control: a secret split across fields or
lines, encoded (non-PEM base64, hex, percent), written without a separator, or short passes it (D397).
