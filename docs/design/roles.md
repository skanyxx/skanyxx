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

¹ Not studio agents (change them with a Studio proposal) and not system agents (seed, skanyxx-factory, skanyxx-*,
preview-*, ticket-flow stages): those answer 409 for everyone, the owner too (D252).
