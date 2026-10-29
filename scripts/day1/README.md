# Day 1: the admin connects the integrations

Requirements §5: git, CI, Argo CD and model providers, set up by the owner and checked healthy through Skanyxx.
Runs after Day 0 on the same target; idempotent.

```bash
scripts/day1/day1.sh up                       # Gitea container, Argo CD, Studio settings, CI runner, models, verify
scripts/day1/day1.sh up --ci emulate          # no runner: Day 2 posts the workflow's check itself
scripts/day1/day1.sh up --argo off            # deploy status from the reconciler and kagent instead
scripts/day1/day1.sh verify                   # the checks only
scripts/day1/day1.sh down                     # removes the Gitea and runner containers and Argo CD
```

| Integration | How | Checked |
|---|---|---|
| Git | A throwaway Gitea (`ws2-ops-gitea`, 127.0.0.1:3430, Actions on); account `skanyxx-bot` (not an admin) with a write token for Skanyxx and a read-only token for Argo, both 0600 files in `$STATE` | `GET /api/studio` has the repo and no repo problem; the repo is private with `main` protected |
| CI | The owner's `POST /api/studio/ci/install` commits `.gitea/workflows/validate-agents.yaml`; the repo gets `SKANYXX_URL` and the secret `SKANYXX_CI_TOKEN`; a Gitea `act_runner` container runs jobs in `node:20-bookworm` | the workflow is in main; `/api/studio/ci/validate` refuses a wrong token and checks with the right one; a builder may read the template, not install it |
| Argo CD | v3.5.6 into `argocd` (refused when the cluster already runs one): plain http, the admission policy `argocd-agent-policy.yaml` (Argo may not write Agents: it only reports, D546), a read-only account `skanyxx` (`applications, get` on this Application only), the kagent Agent health check (`docs/templates/agents-repo/argocd-agent-health.lua`), the repo Secret and `docs/templates/agents-repo/argocd-application.yaml` (an AppProject that allows only kagent Agents in the release namespace from the agent repo, and the Application) | the Application is Synced and Healthy; Argo's controller cannot create, change or delete an Agent; Skanyxx's token reads it and cannot sync or delete it |
| Models | `POST`/`PUT /api/models`: `default-model-config` (Ollama `$OLLAMA_MODEL` on this machine) and `e2e-test-openai` with its key from `$MODEL_KEY_FILE` (default: a generated dummy key, 0600) | both accepted by kagent; the key is write-only (never in a response); a supervisor reads, cannot write |

Skanyxx gets its settings by `helm upgrade --reuse-values -f $STATE/day1-values.yaml` (Studio git/namespace/memory
URL, `RequireChecks`, the skill registry, Argo's URL; egress to the Gitea container, Argo's API and Ollama) and the
Secret `skanyxx-studio` (git, CI and Argo tokens, created from the files with server-side apply).

With `--context`, the scripts refuse an `argocd` namespace or a release they did not create (a ConfigMap
`skanyxx-e2e-owner` marks what they made; `down` removes only that) and reuse only containers labelled `skanyxx-e2e=day1`.

Notes: pods reach this machine as `host.docker.internal`; Ollama refuses a Host header that is a public-range IP
(OrbStack's 0.250.250.254), so the model's host is the name. The dummy OpenAI key makes kagent accept the
ModelConfig; it cannot answer. Give a real key with `MODEL_KEY_FILE=/path/to/key`.
