# Roles (in one UI)

D004 was owner / builder / employee. D024 adds **Agent supervisor**. Same product, four hats. One person may wear several.

| Role | Day-one job |
|---|---|
| **Owner** | First login, model credentials, invite users, optional Entra connect, group → role map |
| **Agent supervisor** | Merge factory/studio PRs; fleet is their responsibility |
| **Builder** | Propose agents (chat factory or studio); cannot merge |
| **Employee** | Chat with the seed (and later agents); memory cards, not YAML |

Git: setup **creates** `skanyxx-agents` (D023). Customer git if they hand us an org/token; otherwise git comes in the stack. Attach an existing repo later.
