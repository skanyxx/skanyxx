# Ticket pipeline stage agents (kagent 0.10.x)

Five `kagent.dev/v1alpha2` Agents, one per stage kind of the Skanyxx Tickets pipeline (default pipeline
`ticket-fix`: plan → review-plan → code → qa-code → review). Skanyxx orchestrates; each stage attempt is one
A2A `message/send` to `{kagent}/api/a2a/kagent/<agent>/`.

    kubectl apply -k deploy/kagent/ticket-flow/

- Requires a `ModelConfig` named `default-model-config` in the `kagent` namespace (change `modelConfig` to use another).
- Pinned to kagent **0.10.x**: the 1.0 alphas drop the `Agent` CRD for `AgentTemplate` + `Harness` (`v1alpha3`).
- A QA or review agent's verdict is the FIRST line of its answer, compared exactly: `VERDICT: PASS` / `VERDICT: FAIL`,
  or `RECOMMENDATION: SHIP` / `RECOMMENDATION: SHIP WITH CHANGES` / `RECOMMENDATION: DO NOT SHIP`. Nothing else in the
  answer is read. Editing a system message is fine; changing those lines silently turns verdicts into `unknown`.
- No tools on purpose: the pipeline proposes changes as text and never writes to a repository or to Jira.
- **After deploying, check the verdict line once:** send `ticket-qa` a message (kagent UI or A2A) and confirm its
  answer's first line is exactly `VERDICT: PASS` or `VERDICT: FAIL`. If the model puts anything before it (a heading,
  bold, a preamble), every QA and review stage reads `unknown` and fails — safe, but each loop is paid.
- Reasoning ("thinking") output is fine: kagent marks thought parts (`kagent_thought` / `adk_thought`) and Skanyxx
  ignores them — the verdict is the first line of the answer, not of the model's thinking.
- Do not put PgBouncer in transaction mode between Skanyxx and Postgres: the run worker holds a session advisory lock.
