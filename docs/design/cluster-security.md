# Cluster security (umbrella chart)

Who may talk to whom inside one install (`deploy/helm/skanyxx`), with what credential, and what we cannot enforce yet.
Checked by `scripts/helm/validate.sh` (C1–C6, plus D137/D138/D148/D188/D194 checks). Sources:
`~/Desktop/ai/active/2026-10-26_2330_skanyxx-phase1-cleanup-rbac/research/kagent-a2a-auth.md`.

## Trust boundaries

```
 browser ──https──> ingress controller ──8080 (allow-listed paths, never /mcp)──> Skanyxx
                                                                                   │  │  │
           ┌──── 8083 (X-User-Id, no auth) ─────────────────────────────────────────┘  │  └── 5432/9000/3000/6379 ─> stores
           v                                                                         │
   kagent controller ── 8080 A2A (no auth) ──> agent pods ── 8081 /mcp/memory (agent secret) ─> Skanyxx
           ^                                     │  └── 443 minus clusterCidrs ─> model provider
           └──────── 8083 (sessions, tasks) ─────┘
 Skanyxx ── grpc 8080 (no auth) ──> ax-server ─> Redis/Dragonfly;  ax-controller ── TLS + SA token ──> Substrate
 sandboxes (WorkerPool ns) ── 8082 /mcp/memory (signed sandbox token) ──> Skanyxx; DNS; internet only if opted in
```

- **Real authentication** happens in two places only:
  - Skanyxx itself: cookies/OIDC on 8080.
  - `/mcp/memory`: an agent secret on 8081 (D083), or a signed sandbox token on 8082 (D190).
- **Everything else is fenced by NetworkPolicy:** the kagent controller, agent A2A, ax-server and the stores. The
  namespace is default-deny (D137).

## Who talks to whom

| From | To | Port | Credential |
|---|---|---|---|
| Ingress controller | Skanyxx `http` | 8080 | User session (cookie / OIDC) |
| Skanyxx | kagent controller | 8083 | None; the user is `user_id` / `X-User-Id` (D101); optional static `KAgent:Token` bearer, not checked |
| kagent controller | Agent pods (A2A) | 8080 | None; the controller forwards `X-User-Id` |
| Agent pods | kagent controller | 8083 | Projected `kagent` token (decoded, not verified) + `X-Agent-Name` |
| Agent pods, kagent controller | Skanyxx `mcp` | 8081 | Per-agent bearer secret; `X-User-Id` only with `actsForUsers` |
| Sandboxes (WorkerPool ns) | Skanyxx `sandbox-mcp` | 8082 | Signed sandbox token only, no headers read |
| Skanyxx | Postgres / MinIO / Gitea / Dragonfly | 5432 / 9000 / 3000 / 6379 | Per-role password / access key / admin token / password |
| Skanyxx | ax-server | 8080 gRPC | None (AX #376) |
| ax-controller | Substrate | 443 | TLS + projected SA token (audience `api.ate-system.svc`) |
| kagent controller | Postgres | 5432 | Own role (`kagent-postgres-url`) |

## ServiceAccounts and RBAC

| Workload | SA | API token | RBAC |
|---|---|---|---|
| Skanyxx | `<release>-skanyxx` | none | none: it uses kagent's HTTP API, never the Kubernetes API |
| Postgres, MinIO, Gitea, AX's Redis | namespace `default` | none (pod-level off) | none |
| Dragonfly | `dragonfly` (umbrella's own) | none (SA-level off) | none |
| ax-server, ax-controller | own SAs | none; ax-controller gets a projected Substrate-audience token only | none (no ClusterRole, D186) |
| kagent controller | `kagent-controller` | yes (it reconciles) | Role + RoleBinding getter/writer in the release namespace only (D136); still every Secret there |
| kagent agent pods | `kagent-agent` (umbrella's, via `controller.agentDeployment.serviceAccountName`) | none; kagent's own `kagent-token` projected volume stays | none; an Agent CR may name another SA |
| kagent UI | `kagent-ui` | chart default | 0 replicas |

Every umbrella workload runs with the same pod security baseline:
- runAsNonRoot and seccomp `RuntimeDefault`
- no privilege escalation
- read-only root filesystem
- all capabilities dropped

kagent's own pods get the same baseline from its global defaults.

## NetworkPolicy matrix (bundled install)

| Pod | Ingress from | Egress to |
|---|---|---|
| Skanyxx | `http`: `ingressNamespaces` (+`ingressPodLabels`) while ingress is on, `httpFrom`<br>`mcp`: kagent controller, agent pods, `mcpFrom`<br>`sandbox-mcp`: WorkerPool ns | DNS; kagent controller 8083; Postgres, MinIO, Gitea, Dragonfly; ax-server (or `ax.namespace`); internet minus `clusterCidrs` on `skanyxxEgress.internetPorts` (443/465/587); `skanyxxEgress.extra` |
| kagent controller | 8083 only: Skanyxx, agent pods, kagent UI if `ui.replicas > 0`, `controllerFrom` | open (Kubernetes API), or `controllerEgress` (D229) |
| Agent pods | controller on `a2aPorts` (8080) | DNS; controller 8083; Skanyxx `mcp`; `modelEgress` |
| Postgres | Skanyxx, kagent controller | DNS |
| MinIO, Gitea | Skanyxx | DNS |
| Dragonfly | Skanyxx, ax-server/ax-controller (or `ax.namespace`) | DNS |
| ax-server | Skanyxx (grpc) | its Redis |
| ax-controller | nothing | its Redis, Substrate ns, WorkerPool ns |
| Sandboxes (WorkerPool ns) | not fenced by us (Substrate's) | DNS; Skanyxx `sandbox-mcp` (if memory on); internet minus `clusterCidrs` (if `ax.sandboxes.internet`) |

- **BYO kagent or Postgres** needs a `skanyxxEgress.extra` rule; the chart refuses a render without one. On-prem
  Jira, IdP or SMTP also go in `extra`.
- **`skanyxxEgress.enabled=false`** restores open egress.
- **The Ingress** targets `http` only, through an allow-list of paths. `/mcp` is refused in any case (D138, D145).
- **`kubectl port-forward`** enters the pod's network namespace, so these policies do not apply to it. Guard
  `pods/portforward` with Kubernetes RBAC.

## kagent / A2A auth status

- kagent 0.10.2 has `unsecure` and `trusted-proxy` only, and authorizes nothing (`NoopAuthorizer`).
- `trusted-proxy` does not verify the JWT signature, and it ignores `X-User-Id` on user calls. With Skanyxx that would
  turn every Chat user into one person, or a 401. **The schema pins `kagent.controller.auth.mode: unsecure`.**
- **Compensating control:** only Skanyxx and agent pods reach 8083, and only the controller reaches agent A2A.

## Known gaps

- **Agent pods can call the whole controller API as any user.** An LLM-driven agent could list or create agents, or
  read other users' sessions. NetworkPolicy cannot filter paths, so closing this needs a mesh L7 policy or kagent authz.
- **The kagent controller can read every Secret in the release namespace.** That includes Skanyxx's DB credentials and
  sandbox token key. Moving Skanyxx's Secrets to another namespace would need a split install.
- **The controller's egress is open** unless `controllerEgress` is on (D229).
- **ax-server and Substrate's atenet-router have no caller auth.** ax-server is fenced to Skanyxx; the router is
  Substrate's responsibility.
- **Correctness depends on the CNI enforcing NetworkPolicy.** kindnet, Calico and Cilium do; flannel alone does not.
- **Template-validated only.** The tighter rules (C1–C5) have not been run on a live cluster yet.
