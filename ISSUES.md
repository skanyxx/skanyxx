# ISSUES — agents CRUD (branch `agents-crud`)

## FOUND

| ID | Symptom | Evidence | Where | Severity |
|---|---|---|---|---|
| A1 | Editing an agent silently drops tool-level `headersFrom` (Secret-backed `Authorization`) and `isolateSessions`; after a save, MCP calls go out unauthenticated | Code review; kagent v0.10.2 `Tool` has `HeadersFrom`/`IsolateSessions` on the entry, not on `mcpServer` (`agent_types.go:554-589`); PUT replaces the whole spec | `Manage/Definition/AgentSpec.cs` `Tools()` (as of f33b8df) | Major |
| A2 | A direct agent could name `seed`, `skanyxx-factory`, studio agents or ticket stages as agent tools; anyone who chats with it then drives the seed's memory identity or the factory | Code review; form options listed every agent | `Manage/Definition/AgentFormRules.cs` `CheckAgentTools`, `AgentFormOptionsHandler.cs` (f33b8df) | Major |
| A3 | Delete and edit worked in any namespace kagent watches, while create was limited to `Studio:Namespace`; list offered Edit there, which then failed 400 | Code review | `DeleteAgentHandler.cs`, `UpdateAgentHandler.cs`, `ListAgentsHandler.cs` (f33b8df) | Minor |
| A4 | An edit could report 404/502 after kagent applied it (extra re-GET after PUT) | Code review | `UpdateAgentHandler.cs` (f33b8df) | Minor |
| A5 | Agent tools / MCP servers referencing another namespace or kind were retargeted to the agent's namespace on edit | Code review | `AgentSpec.cs` `Tools()` (f33b8df) | Minor |
| A6 | Audit details held a lazy enumerable | Code review | `AgentAudit.cs` | Nit |
| A7 | Razor pages that never changed (People, Sandboxes, Studio) fail to compile with nonsense RZ1021/CS0501 errors, even on a clean HEAD | `dotnet build` with the shared compiler server; clean after `dotnet build-server shutdown` or with `-p:UseSharedCompilation=false` | build environment (shared `VBCSCompiler`/Razor server used by parallel sessions) | Env |
| A8 | Old Agents page rendered kagent names/descriptions with `innerHTML` (stored XSS from an agent description) | Reading `Agents.cshtml` at b57ed75 | `src/Skanyxx.Host/Pages/Agents.cshtml` (b57ed75) | Major |

## FIXED

| ID | Commit |
|---|---|
| A1–A6 | 0a93dd1 (D256; tests `AnAgentsExistingTools_…`, `SystemAndStudioAgents_AreNeverToolsOfANewAgent`, `AgentsOutsideTheStudioNamespace_…`) |
| A7 | worked around: build with `-p:UseSharedCompilation=false` |
| A8 | 68a35c9 (page rewritten, textContent only) |
| Codex C-1…C-8 | see FIX_REPORT.md (854f5b3, a0b7674, 9a4c14f, b36749f, 073b0c1, b801ab3, 1f4a6b4, 88e5fe5) |

## OPEN

| ID | What | Why open |
|---|---|---|
| O1 | Lost update, residual (C-2): stale forms are now 409 (spec version), but a write landing between Skanyxx's read and kagent's own Get+Update is still lost | kagent 0.10.2's PUT ignores the body's resourceVersion; needs upstream support |
| O3 | List shows Edit for agents whose prompt comes from `systemMessageFrom`/foreign tools; the details view and the API refuse (409) | The list API from kagent is summarised to `Agent` without the spec; owner may want a richer list |
| O4 | Studio write-back: a direct edit of a studio agent is blocked (D252), not turned into a PR | requirements.md §3.3 "open decision: block, or write back as a PR" — block chosen; PR write-back needs owner go-ahead |
| O5 | Suspend/resume now exist (C-7, D257); still no "unpublish from Chat" without stopping the agent, and resume returns to one replica | Labels and annotations are immutable through kagent's PUT |
| O6 | A direct agent's name blocks a later studio merge of the same name (reconciler never adopts) | By design (D252); owner may want a rename/adopt flow |
| O7 | D-row numbers D250–D256 chosen to avoid collisions with parallel branches (all at D235); may need renumbering at merge | Merge coordination |
