# Day 2: people make an agent and use it

Requirements §5: a user creates an agent, gives it instructions, a skill and a knowledge base, talks to it and finds the
conversation again. Runs after Day 1 on the same target (same `--ci` / `--argo`).

```bash
PLAYWRIGHT_MODULE=/path/to/node_modules/playwright scripts/day2/day2.sh run   # screenshots too (real Chrome)
scripts/day2/day2.sh clean                                                     # every e2e-test-* object goes
scripts/e2e/studio-cicd.sh                                                     # the Studio CI/CD path alone
scripts/e2e/run-all.sh --down                                                  # Day 0 → 1 → 2 → CI/CD → clean → teardown
```

`run` (`day2.mjs`, results in `$STATE/day2-result.json`):

1. People: the owner invites a supervisor, a builder and an employee (passwords generated into `$STATE/people.env`).
2. Knowledge base: the supervisor writes two company cards (a fact and a procedure document) holding facts only they
   know; an employee may not write company cards, finds both in the Library.
3. Studio: the builder takes the "support" template, renames it `e2e-test-support`, adds instructions, the skill
   `$SKILL_REF` (`ghcr.io/agentregistry-dev/skills/brand-guidelines`, digest-pinned) and a company search grant, checks
   the YAML preview, saves it as a pull request; the PR holds exactly the two agent files; the builder cannot merge; a
   merge before CI is refused (`Studio:RequireChecks`); the workflow passes on the runner; the supervisor merges.
4. Deploy: the reconciler applies on merge; kagent's Agent is Ready (its skill pulled), labelled merged; Argo CD
   reports it Synced + Healthy against main without ever syncing (D546); the Studio timeline says Deployed, from Argo CD.
5. Chat: the employee sees the agent, asks what only the cards say (37 days, code word `PELICAN-42`), asks again in the
   same conversation, finds it in their history and reopens it; another person cannot read it.
6. UI: `/Studio` shows the change deployed; the employee's Chat shows the agent with its skill and memory access, and
   their menu has no Studio, Models or People.

`clean` (`clean.mjs`): agent folders leave main by one pull request (the studio has no "remove" yet, D115) and the
owner confirms the removal pass (D119); conversations deleted; cards marked stale (memory keeps versions); e2e-test-*
models deleted; e2e-test-* people disabled (a later `run` enables them again).
