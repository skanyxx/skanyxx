# Chrome test: phase1-cleanup-rbac (2026-10-28)

**Result: PASS.** 32 of 32 Chrome checks passed after one bug was fixed. Full test suite: 2203/2203 passed.

## What was started

| What | How | Stopped after |
|---|---|---|
| Postgres | Throwaway container `crud-test-pg-phase1` (`postgres:16-alpine`) on `127.0.0.1:55491`, database `skanyxx`. Reset between runs | yes (`docker rm -f`) |
| Host | `dotnet build Skanyxx.sln -p:UseSharedCompilation=false`, then `dotnet run --no-build --project src/Skanyxx.Host --no-launch-profile` on `http://127.0.0.1:5391`. Development, env config as in `scripts/dev/first-hour.sh host`. `KAgent` = `localhost:8083`. Tickets from the sample file | yes |
| kagent | Already running: controller on `localhost:8083`, UI on `8080`. Not started, stopped or changed. `seed` and `skanyxx-factory` were only read (Agents page) | n/a |

Studio git (Gitea `:3300`) was not running, so the Studio page loaded without git configured. The other worktree's Host on `:5287` was not touched.

## How

Playwright (`channel: "chrome"`, i.e. `/Applications/Google Chrome.app`, headless), run as a throwaway script outside the repo. A copy is in the evidence folder (`run.mjs`).

1. `/Setup` creates the owner `crud-test-owner@example.com`.
2. `/Login` signs the owner in, then every panel is opened.
3. The owner's API invites `crud-test-employee@example.com` with role `employee`, and the invite is accepted.
4. The employee signs in through `/Login`, then opens their own panels and every admin panel.

Each panel was checked for:
- an HTTP status of 200, or 403 where access should be denied
- no console errors or page errors
- no 5xx responses on the network (the Host log also had none)
- no secrets in the HTML: neither test password, nor any `Bearer …`, `sk-…` or JWT

Every panel got a full-page screenshot.

Both accounts and their data lived only in the throwaway database, which was deleted afterwards.

## Panels

| Panel | Owner | Employee |
|---|---|---|
| Setup, Login, sign-in | PASS | PASS (sign-in) |
| Agents, Chat, Library (memory cards), Tool Servers, Account | PASS | PASS |
| Studio, Plugins, Sandboxes | PASS | denied, 403 "Not allowed", PASS |
| Debug, Model, People, Org, Microsoft sign-in (Entra), Audit | PASS | denied, 403 "Not allowed", PASS |
| Sidebar for the employee | n/a | shows Agents, Chat, Library, Tool Servers only. No admin entries, PASS |

Screenshots: `~/Desktop/ai/active/2026-10-26_1941_skanyxx-release-diagnostic/evidence/chrome/phase1-cleanup-rbac/` (32 PNGs plus `results.json`).

## Bug fixed

**A signed-in person opening a page their role lacks looped forever** (Chrome: `ERR_TOO_MANY_REDIRECTS`, first seen on `/Studio` as the employee).

- **Cause:** the cookie's `AccessDeniedPath` was `/Login`. `/Login` sends a signed-in visitor back to `ReturnUrl`, which is the forbidden page again.
- **Fix:** `AccessDeniedPath` is now `/AccessDenied`, a new anonymous page that answers `403` "Not allowed" with a link home. APIs still answer a `403` ProblemDetails. Recorded as D236; the README is updated.
- **Regression test:** `FirstHourTests.Employee_OnAPageTheirRoleLacks_GetsA403Page_NotALoginLoop` (`/Studio`, `/Sandboxes`, `/People`). It fails 3/3 with the old setting.
- **Other tests:** six existing assertions (in five test files) pinned the old `/Login` target for signed-in forbidden pages (People, FirstHour ×2, Studio, Plugins, Sandboxes). They now expect `/AccessDenied`. Anonymous visitors still go to `/Login`.
- **QA:** an independent review returned PASS, with two nits applied and one skipped. Saved in the task dir under `review/code-reviewer-phase1-cleanup-rbac-1.md`.

## Tests

`dotnet test Skanyxx.sln` (after the fix, Docker on OrbStack): **2203 passed, 0 failed, 0 skipped** (2203 total). The first full run, before the six pinned assertions were updated, had 2200/2203; its 3 failures were exactly those Studio/Plugins/Sandboxes assertions.
