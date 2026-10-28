# Skanyxx: owner requirements (2026-10-27)

**Status:** binding. These are the owner's (Anatoli's) product requirements as of **2026-10-27**. They come from the
owner's brief (`~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/skanyxx-product-brief.md`).
**If this file conflicts with an older file in `docs/design` (decisions.md, todo.md, roles.md, memory-*.md, studio.md,
first-hour.md, identity.md, facts.md and the rest), this file wins.** When a slice implements one of these items, record
the decision in `decisions.md` as a new row that cites this file (`requirements.md §N`). Do not edit old rows.

Skanyxx is an orchestration and integration UI built on [kagent](https://github.com/kagent-dev/kagent) and
[Google AX](https://github.com/google/ax): a .NET 10 modular API plus a web UI, with a Helm umbrella chart.
**It is not an SRE IDE.** Remove whatever is left from the SRE IDE era instead of keeping it as dead code.

Status words used below: **Done**, **Partial**, **Missing**, **Remove**.

---

## 0. Overrides of older docs (read this first)

| # | Older position (file) | Owner requirement now |
|---|---|---|
| O1 | Chat history lives only in kagent sessions; "cards, not chats" (`memory-store.md`, `three-planes.md`, D012, D050) | Every chat session is **indexed in Skanyxx Postgres**: user, agent, created/updated time, title, **keywords, categories, ticket links (e.g. Jira keys), and a short outcome summary**. When a new chat starts, Skanyxx searches the index and the memory cards for **related prior work** that the user is allowed to read. It injects a compact summary, with a bounded token budget, so the agent does not redo work. Transcripts are still **not** copied into team, department or company memory. Only cards are lifted (D050 still holds for transcripts). |
| O2 | Microsoft Entra sign-in is an optional slice that is always visible to the owner (`identity.md`, D026/D027/D093–D096) | Microsoft sign-in is **off by default and hidden**. It is enabled only for a specific customer through an explicit per-install switch. With the switch off, no Entra page, nav item, login button or endpoint is exposed. |
| O3 | SRE pages were kept as "leftovers" (`facts.md`, D001) | All SRE features are **removed**: quick commands, kubectl/aws/az runners, alerts, hooks, SRE dashboard, SRE investigate, SRE cloud tools, and SRE analytics. The product panels with those names (Dashboard, Investigate, Cloud tools, Analytics) are **rebuilt for the new product**. The requirements are in §3. Removing the SRE version does **not** remove the panel from the product. |
| O4 | "Chat is the only place that talks to an agent" (D002, D017) | The rebuilt **Investigate** panel runs **nested agent workflows**, as described in §3.2. Chat is still the only surface for free-form conversation. |
| O5 | After setup, land on Chat, "not Dashboard" (`todo.md`, `first-hour.md`) | Admins land on the **admin Dashboard** (§3.1). Everyone else lands on Chat. First-hour setup still goes `/Setup` → `/Model` → Chat. |
| O6 | Agents API is read-only after Phase 1 cleanup | Agents need **full CRUD by role**, like kagent-ui (§3.3). The Phase 1 removal of kubectl-based writes is correct. The new writes go through the kagent controller API with server-side RBAC. |
| O7 | Dragonfly is "cache/locks later, not orchestration" (D042/D043/D053) | Still not an orchestrator. Dragonfly is also used as an **answer cache for repeated questions** (§3.6). It is behind a config flag, scoped to agent and access scope, and has a TTL. |
| O8 | Four roles: owner/supervisor/builder/employee (`roles.md`) | The roles stay, but the role model is **hard, deny-by-default RBAC** (§2), and **panels are gated per role**. Mapping to the brief's words: *admin* = owner (plus any admin role added later), *maintainer* = supervisor (and builder for proposals only), *user* = employee. |

---

## 1. Cross-cutting rules

1. **No SRE leftovers.** No page, module, endpoint, setting, config key, installer step, test or doc may still describe
   Skanyxx as an SRE/NOC tool or run cluster or cloud commands from the Host. Anything still in use from that era is
   renamed or reshaped. No compatibility shims.
   *Accept:* `git grep -i -E "sre|kubectl|quick command|alerts|hooks"` in `src/`, `tests/`, `installer/` and `deploy/`
   finds only legitimate kagent/k8s deployment uses. The removed modules do not exist in `Skanyxx.sln`.
2. **`docs/` is the reference.** Every panel below has a design note in `docs/design` that matches the code. This file
   is the requirements layer above them.
3. **Security first** (§2). The UI hides what a role cannot do, but **the server is the control**.
4. **Sensitive data never leaks.** Secrets, tokens, API keys, passwords and PII never appear in API responses, logs,
   audit records, error messages, the UI, or build and release artifacts. Secrets are write-only. Read-backs say only
   "set" or "not set".
   *Accept:* automated tests or guards cover each channel. The release payload guard rejects artifacts that contain secrets.
5. **Cost and token numbers are informational.** Every place that shows money shows a prominent disclaimer (§3.5).

## 2. Security: hard RBAC, in-cluster and A2A

- **Role model:** at least admin (owner), maintainer (supervisor), builder, and user (employee). One person may hold
  several roles. `docs/design/roles.md` must contain a roles × panels × actions table that matches the code.
- **Deny by default:** every API endpoint requires authentication and an explicit role or policy. Anonymous access is
  allowed only on login, setup, invite, password reset and health. A new endpoint without a policy fails a test.
- **Non-maintainers can do no harm:** a regular user cannot create, edit or delete agents, models, tool servers, orgs,
  users or pipelines owned by others. They cannot change settings beyond their own preferences, and they cannot read
  other users' chats or private cards.
- **Panel gating:** panels a role cannot use are not rendered in the nav, and their server routes return 403/404.
- **In-cluster:** least-privilege ServiceAccounts and k8s RBAC in the chart. NetworkPolicies fence Skanyxx, the kagent
  controller, agent A2A, AX and the stores. Agent pods use tokenless SAs.
- **A2A:** calls between Skanyxx, the controller and agents carry an authenticated identity. Where kagent cannot verify
  it yet (0.10.x `unsecure` mode), the gap is documented in `cluster-security.md` with a mitigation and a tracking item.
- *Accept:* there are tests where each lower role calls every privileged endpoint and gets 401/403. There are tests
  that panels are hidden per role. `scripts/helm/validate.sh` passes. `cluster-security.md` is current.

## 3. Panels

### 3.1 Dashboard (admin)
Management overview for admins: system health (kagent, AX, Postgres, MinIO, Dragonfly, git), user and agent usage,
token use against quotas, and errors. It uses **compact graphs**.
*Accept:* visible to admins only. Data comes from Skanyxx's own stores and health checks, with no SRE cluster commands.
It loads in under 2 s with 1k users.

### 3.2 Investigate (rewritten)
This is not the SRE investigate page. It represents **nested agent workflows** that match customer workflows (e.g. ticket →
plan → code → review → QA, which today is the Tickets pipeline engine). Users get **full CRUD on workflows**: create,
edit, delete and run, with stages and nested agents. Each run produces a **PDF and/or JSON report** and also **a card in
the memory bank** that follows the card schema and scope rules.
*Accept:* there is a UI over pipelines and runs (none exists today). Who can create, edit or delete a workflow is
role-gated. A finished run writes a card in the run owner's scope, which can be lifted like any card. The report can be
downloaded as PDF and JSON.

### 3.3 Agents
Full agent CRUD by role, **like kagent-ui**: list, details, create, edit, delete, and enable/suspend. It works through
the kagent controller API. Reference: `kubectl -n kagent port-forward svc/kagent-ui 8080:8080` and
`svc/kagent-controller 8083:8083`.
*Accept:* maintainers and admins can perform all operations. Users can read the agents they are allowed to use. Every
write is audited and enforced server-side. Studio-made (GitOps) agents show their repo/PR origin, and a direct edit
cannot silently diverge from git (open decision: block, or write back as a PR).

### 3.4 Chat
- An agent picker like kagent UI, with **richer Agent Details** (description, model, tools, memory scope and owner).
- **Voice input** (speech to text) and **file attachments**.
- **History:** a list of the user's own past sessions that they can reopen and continue.
- **Continuity:** reloading or staying on the same page **continues the same session**. A reload never creates a new one.
- **Chat index and related context (O1):** each session is indexed (keywords, categories, ticket links, outcome summary,
  FTS). On the first message of a new chat, related prior sessions and cards the user may read are injected within a
  strict token budget. The response tells the user which prior sessions or cards were used. A team on ticket 64 can find
  chats and runs from ticket 58 on the same topic by ticket key, keywords or related tickets.
- **Access control:** a user only sees, or gets injected with, sessions they may read: their own personal sessions, plus
  team, department or company content per the memory AccessPolicy. **Another person's private chat never leaks.**
*Accept:* tests for reload continuity, the history list (own sessions only), and the injection budget. There are
access-control tests that user B never sees or gets injected with user A's private session.

### 3.5 Token quotas and cost (new)
- Admins set token quotas per **user, team, department** and organization.
- Users see their usage as a **percentage of their quota**. Usage counts the **model-side tokens consumed** (input,
  output, tool and reasoning, as the provider reports them, like `/usage` in Claude), not just client-side counts.
- Over quota: the request is blocked or limited per policy, and the user can file an **open request to the admin** to
  raise the quota. The admin approves or denies it from the Dashboard or People view.
- **Cost estimate** per user, team or org, if it can be derived from model pricing. It is always shown with a big
  disclaimer: **"Estimate only. Not accurate. For information only. Your provider's bill is authoritative."**
*Accept:* quotas are enforced server-side before the model call. Usage is stored per session and per turn. The
disclaimer appears everywhere cost appears, including exports.

### 3.6 Memory bank and cache
- A memory bank **like [Joicy](https://github.com/DmarshalTU/Joicy), fully implemented**: personal → team → department →
  company hierarchy, card/body/source layers, lift, supersede, versions, grants, and agent access over MCP. The parts
  of the existing memory-*.md design that are still open must be finished, not left partial.
- **Dragonfly answer cache:** repeated identical questions to the same agent within the same access scope are served
  from cache instead of spending tokens. It is behind a config flag, has a clear TTL, and is invalidated when the agent,
  model or relevant cards change. A cached answer is never served across access scopes.
*Accept:* tests for scope isolation of the cache and for hit/miss/invalidation. A token-savings metric is shown on the
Dashboard.

### 3.7 Library
Library is built to scale to many cards. It has **better filters**: scope, team, department, owner, agent, ticket,
category/keyword, date, and type. It also has sort, paging and saved filters, so company and management users do not
get lost.
*Accept:* filters combine server-side with paging. Each filter is checked for access.

### 3.8 Studio (non-coders too)
Flow: open Studio → describe or define the agent → render from a **template** (agents are microservices) → create or
use a **git repo and push a PR** → after approval, **CI/CD (Helm chart or deployment YAML) deploys it** to the cluster,
already connected to the kagent controller.
*Accept:* someone who does not code can complete the flow without editing YAML. Approval is limited to maintainers.
The deployment status is visible in Studio.

### 3.9 Cloud tools (paid)
Cloud tools are **in-house tools that users or companies can buy and enable** (a pay-to-win catalog). This is not the
old SRE cloud-command page.
*Accept:* a catalog with entitlements. Install and enable are gated by entitlement and role. It can build on the plugin
system.

### 3.10 Tool servers
Tool servers work **like kagent UI**: list, details, create, edit and delete MCP servers, and see their tools.
*Accept:* writes are maintainer/admin only. Secrets are write-only.

### 3.11 Analytics
Users see **their own** usage (chats, agents, tokens, quota). Admins can also see the **team, department and
organization** views.
*Accept:* aggregation respects access scope. Users cannot view other users' analytics.

### 3.12 Debug
Pure diagnostics: version, mode, modules, health checks, recent errors with secrets redacted, and connectivity.
**No quick commands** and no command execution. Admin only.

### 3.13 Settings
More customizable for both **users** (theme, default agent, voice and input options, notifications) and **admins**
(integrations, quotas defaults, feature switches such as Entra and the answer cache, retention TTLs).
*Accept:* per-user settings are stored server-side. Admin settings are audited, and secrets are write-only.

### 3.14 Models
Models get full CRUD **like kagent UI**: create, edit and delete ModelConfigs, choose the provider, model and secret,
and set a default. Today only a single model step exists.
*Accept:* admin only. API keys are write-only.

### 3.15 Users and Org
- **User management:** invite, disable/enable, role change, and **password reset** (admin-issued and self-service "forgot").
- **Org panel: full CRUD** on departments, teams and memberships, including delete with safe handling of members and scopes.
- **Microsoft sign-in:** off by default (O2).
*Accept:* every action is audited, and RBAC tests cover it.

## 4. Deployment
- **Now:** a desktop application that is given a kagent controller URL (installers for macOS, Linux and Windows).
- **Later:** baked into the Helm umbrella chart, where **only the Skanyxx UI is exposed via ingress**. Everything else
  stays cluster-internal.

## 5. QA: first real customer scenario
- **Day 0:** install the infrastructure on the customer's k8s with the umbrella chart, then run the end-to-end
  integration tests.
- **Day 1:** the admin gets access and sets up the external integrations: git, Argo, model providers, Jira, etc.
- **Day 2:** a user creates an agent, talks to it and solves problems. They add skills, documents and instructions,
  for example their knowledge base, so agents and models can use it.
*Accept:* each day has a scripted, repeatable test run with evidence, and failures block release.
