# Chrome test: `chat-context` (2026-10-28)

## What was started (all stopped afterwards)

- **Postgres:** throwaway container `crud-test-pg-chatctx` on `127.0.0.1:55491`, recreated for each run and then removed.
- **Host:** built with `dotnet build Skanyxx.sln -p:UseSharedCompilation=false`, started on `http://127.0.0.1:5391` with the
  env that `scripts/dev/first-hour.sh host` sets: Development, `KAgent` at `localhost:8083` with a 300 s turn, the
  sample tickets file, and all three connection strings pointing at the throwaway Postgres.
- **kagent:** the existing local controller (`:8083`, UI `:8080`, OrbStack). Its port-forwards were not touched.
  `seed` and `skanyxx-factory` were not modified. The seed's memory MCP points at another checkout's Host (`:5287`),
  so a seed turn fails at `initialize`. The chat turn used a throwaway Agent `crud-test-chat` instead: no tools, labelled
  `skanyxx.dev/merged`, on `default-model-config`. The agent and its sessions were deleted afterwards.
- **Ollama:** it was not running, so the model call failed. It was started for the run and stopped afterwards.
- **Browser:** Google Chrome, driven by Playwright 1.64 (`channel: "chrome"`, headless, 1440×900), from a temp
  directory outside the repo.

## Panels (final run: 40/40 pass)

Each page check covers: HTTP status, no console errors, no 5xx responses, no secrets in the HTML (passwords, bearer or
JWT tokens, `sk-` keys), and one screenshot.

| Who | Checked | Result |
|---|---|---|
| — | `/Setup` (owner created), `/Login` | pass |
| owner | Agents, Chat, Library, Tool Servers, Studio, Plugins, Sandboxes, Debug, Model, People, Org, Entra, Audit, Settings, Account | pass (200) |
| owner | **Chat (this branch):** a real turn on `?ticket=SDB-58` (200; the index row has `ticketKeys: [SDB-58]`); **reload** keeps the same session and shows the question, then the answer; History lists it; `GET /api/chat/sessions?mine=true`, `/api/chat/tickets/SDB-58` (contains the session) and `/api/chat/cache` | pass |
| employee | nav: no Studio, Plugins, Sandboxes, Debug, Model, People, Org, Microsoft sign-in, Audit or Settings | pass |
| employee | Agents, Chat, Library, Tool Servers, Account | pass (200) |
| employee | the owner's personal chat: not in `/api/chat/sessions?ticket=SDB-58`, `GET /api/chat/sessions/{id}` is 404, not in the ticket view | pass |
| employee | Studio, Plugins, Sandboxes, Model, People, Org, Entra, Audit, Settings → `/AccessDenied` (403, "Not allowed") | pass |
| employee | Debug: the page shell is open on this branch, hidden from the nav, and its data (`/api/settings/system-info`) answers 403. Known: `phase1-cleanup-rbac` (`eaa6952`) makes it owner-only | pass (known) |
| owner | cleanup: `DELETE /api/chat/sessions/{id}` | pass |

Screenshots (36), `results.json`, the script `run.mjs`, its log and the Host log are in
`~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/evidence/chrome/chat-context/`.

## Bugs found and fixed

1. **Role-gated pages looped (`ERR_TOO_MANY_REDIRECTS`).** An employee opening `/Studio` (or any page their role
   lacks) was sent to `/Login`, which sends a signed-in visitor back to the `ReturnUrl`. This is the same fix as
   `phase1-cleanup-rbac` `393fc3b`, cherry-picked: `AccessDeniedPath` is now `/AccessDenied`, a 403 page. On this branch
   it is **D243**, because D236 is taken here; when the branches merge, keep one row. Regression:
   `FirstHourTests.Employee_OnAPageTheirRoleLacks_GetsA403Page_NotALoginLoop`.
2. **A reload lost the owner's conversation (D242).** The Chat page read `/api/sessions/{id}/events` without `user`.
   By default an owner reads as the console's own kagent user (D124), which does not have the owner's chats, so kagent
   answered 404 and the page started a new chat. The page now asks with `?user=<own id>`. Regression:
   `ChatContextTests.TheChatPage_ReopensTheOwnersConversation_AsTheOwner`.
3. **Reopened chats showed no messages.** kagent's Go runtime stores events in PascalCase (`Content`, `Partial`,
   `Timestamp`) and lists them newest first, and the page only read camelCase. It now reads both shapes and sorts the
   messages by time. Regression: assertions in `TheChatPage_ContinuesTheConversationAcrossAReload`; verified in Chrome.
   Bugs 2 and 3 are **D244**.

## Tests

The full suite, `dotnet test Skanyxx.sln -p:UseSharedCompilation=false`, run once after the fixes: **2261/2262 passed**.
The one failure, `PluginStudioTests.ValuesStoredWhileACloseLands_AreNotPurgedByIt`, is a timing flake that this branch
did not introduce. The test marks a row as renewed with Postgres `now()`, which uses the OrbStack VM's clock, and
compares it with the close's `DateTime.UtcNow`, which uses the Mac's clock. If the VM clock lags, the row looks older
than the close and is purged. The test came in at the merge base `ae48f25`, before this branch. Rerun alone, the
test class passed 5 times out of 5 (57/57 each time). It was not changed here.
