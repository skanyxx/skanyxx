# Day 0: install on Kubernetes, then smoke it

Requirements §5: the infrastructure goes on with the umbrella chart, then an end-to-end check. One script, idempotent.

```bash
scripts/day0/day0.sh all                      # kind cluster skx-day0 (created), image, ingress-nginx, chart, smoke
scripts/day0/day0.sh all --context my-cluster # an existing cluster, named explicitly (never the current context)
scripts/day0/day0.sh smoke                    # the smoke again
scripts/day0/day0.sh down                     # deletes the kind cluster it created (or uninstalls from --context)
```

`up`: a kind cluster with its own kubeconfig (`$STATE/kubeconfig`: kind never becomes your current context) and
127.0.0.1:`$UI_PORT` (18430) mapped to ingress-nginx; the image from `scripts/helm/build-image.sh`, loaded with
`kind load docker-image` (no registry is touched); `helm upgrade --install` of `deploy/helm/skanyxx` with
`deploy/helm/local/day0-values.yaml` (Ingress on `localhost` and `host.docker.internal`, `/health` published);
every Deployment and StatefulSet rolled out.

`smoke` (`smoke.mjs`, results in `$STATE/day0-result.json`): `/health` Healthy through the Ingress, the UI answers,
`/mcp/memory` is not published, the owner is bootstrapped with the release's bootstrap token (read from its Secret,
never printed; a second bootstrap is 409), kagent answers through Skanyxx (`/api/models`, `/api/chat/agents`), and an
invited employee gets 403 on admin APIs (models, people, invites, settings).

| Setting | Default |
|---|---|
| `--cluster` / `--context` | `skx-day0` (kind) |
| `NAMESPACE`, `RELEASE` | `skanyxx` |
| `IMAGE`, `IMAGE_TAG` | `skanyxx`, `e2e-<git short sha>` (`--rebuild` builds again, `--skip-build` uses what the nodes can pull) |
| `UI_PORT` (kind), `PF_PORT` (`--context`: a port-forward to the Service) | 18430, 5431 |
| `SKX_E2E_STATE` | `~/.config/skanyxx-e2e` (state per target, 0700; secrets in 0600 files) |

Credentials: `$STATE/owner.env` (the owner's e-mail and generated password, 0600) and `$STATE/people.env` (the e2e
people). Nothing secret is printed. With `--context`, `down` uninstalls the release only; the chart keeps its Secrets
and volumes (D139).
