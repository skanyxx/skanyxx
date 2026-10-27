# Step 3 — Agent studio

Two doors, one repo, one gate (D020):

| Door | Who | What they do |
|---|---|---|
| **Studio** | Builder | Form: compose an agent, **Save draft → PR** |
| **Factory** | Builder | Chat: “make a Refund agent…”, same files, same PR |
| **PR merge** | Agent supervisor | YAML review; then it is live in kagent |

Studio is **not** a live kubectl apply button. It is not kagent’s dashboard. Primary UI is the form; YAML is what the supervisor reads.

## What a builder composes (this is the form)

Maps to a kagent `Agent` CR + Skanyxx grants in the same PR:

| Field | Plane | Notes |
|---|---|---|
| Name, description | kagent | Who this agent is in chat |
| Model | kagent `ModelConfig` | Pick one the owner already configured |
| Instructions | kagent `systemMessage` | The job, tone, what it must not do |
| Skills | kagent `skills.refs` / git / A2A | Templates later (step 5); attach now |
| MCP tools | kagent `RemoteMCPServer` + `toolNames` | Which servers, which tools |
| Collection grants | **Skanyxx** | Per collection: **search** and/or **upsert** (D033). Preview never upserts (D032). |
| kagent TTL memory | kagent, optional | Per-agent leftovers. **Not** the company bank (D008) |

If they add a **new** MCP server or skill ref, that YAML is in the **same PR**. Supervisor sees the whole change.

The seed general-purpose agent is just the first file. Changing it uses the same studio + PR path.

## What studio is not

- Not editing memory cards (library / chat seed does that)
- Not inviting users (owner)
- Not merging (supervisor)
- Not a YAML IDE as the happy path (advanced toggle later is fine)

## Preview vs production (locked)

- **Preview chat** on the branch (D030): builder/supervisor only.
- **Employee chat** lists **merged** agents only (D031).
- Preview may **search** granted collections so retrieval can be tested; it **must not write** cards to production (D032).

## As built (2026-10-06, todo.md slice 3)

```
/Studio form (or factory draft) → branch + commit + PR in skanyxx-agents (builder) → preview chat (builder/supervisor)
    → supervisor merge in Skanyxx (owner for team/department grants) → reconciler → kagent Agent labelled merged → Chat
```

- **Repo.** Setup creates `skanyxx/skanyxx-agents` in the bundled git (Gitea locally; `main` protected, only Skanyxx's
  account merges). `agents/<name>/agent.yaml` (kagent Agent) + `agents/<name>/grants.yaml` (memory grants), one PR (D110).
- **Form = D029.** Name, description, model (the owner's ModelConfigs), instructions, skills (OCI refs), allow-listed MCP
  tools, search/upsert per company/team/department scope, optional kagent TTL memory. The memory server follows the
  grants. Factory drafts the same form (D114).
- **Gate.** Builders propose, supervisors merge; files re-checked at the PR head against exactly the form's shape, merge
  pinned to that head; team/department grants need the owner (D111).
- **Reconciler.** kagent's HTTP API only; grants and a studio-issued secret per agent (never acting for users);
  idempotent; removes agents that leave main and previews of closed PRs (D112). One pass at a time across replicas, and
  a failing pass never stops the Host (D120); the secret kagent holds is tracked by fingerprint (D118).
- **Trust.** Only memory principals the studio claimed (D117); one recorded repo, never re-created, private with main
  protected (verified, never re-protected behind the owner's back) and a removal brake the owner lifts (D119); untrusted YAML scanned and size-capped (D123).
- **Owner's stop.** Suspend/resume a studio agent: memory access revoked at once, out of kagent (previews too) until
  resumed (D121).
- **Sessions.** Each person reads only their own kagent sessions; the owner anyone's by name (D124).
- **Preview.** `preview-<pr>-<name>`, not merged, search on `company` only, never upsert, no skills, gone on
  merge/close (D113, D122). Skills only digest-pinned from allow-listed registries (D122).
- Not yet: customer GitHub/GitLab, removing an agent from the UI, the seed in the repo, skills from git (D115).

## Plugins (as built 2026-10-20, D205–D218)

```
/Plugins: catalog | git URL @ commit (owner) | zip (owner) → inspect (report + digest) → install = one PR
    (plugins/<p>/plugin.yaml + plugins/<p>/mcp/<s>.yaml + agents/<a>/) → supervisor merge on /Studio
    → reconciler: RemoteMCPServers (header values from a fingerprinted Secret) then agents → Chat
```

- Claude Code plugins; recipes only, never memory (D205). Remote MCP servers only, stdio never run (D209).
- Header values never in git: templates wait for the owner's value ("needs secret", server not created, agents held);
  values encrypted in memory, given to kagent as `<server>-<fingerprint>` (D208, D211).
- Merged plugin servers join the MCP allow-list and the form (D212). Plugin PRs have their own inspector shape, no
  preview, and the removal brake covers servers too (D213).
- Not yet: OAuth-only MCP servers, updating kagent servers in place (kagent 0.10.2 has no update: replace), a plugin's
  skills/commands/hooks.

## Direct edits on the Agents page (as built 2026-10-27, D250–D255)

The Agents page is kagent-ui's CRUD for agents the studio does **not** manage: supervisors and the owner create, edit
and delete them straight through kagent's controller API, no PR. A studio agent there is read-only and shows its file
(`agents/<name>/agent.yaml`); its change is a Studio proposal, so git never silently diverges (D252). Direct agents
are labelled `skanyxx.dev/managed-by: skanyxx-agents` (the reconciler leaves them alone) and `merged` (in Chat, D253).
A name a direct agent holds cannot later be merged from the studio (the reconciler never takes over an agent it did
not create).
